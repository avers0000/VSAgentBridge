// VS Agent Bridge — Claude Code adapter (mod).
// Before Claude Code edits, writes or reads a file, or runs a shell command, under one of the configured workspace
// roots, asks vsagentbridge.exe (two folders up) what to do with the call: let it through (allow), block it (deny),
// or send it to another path (redirect: a copy under the bridge's Output). When the exe asks for it ("after": true),
// calls it again once the tool has completed, so that a new file is added to its project. The exe's text for the
// model goes into the result's context. Outside the roots, or with no roots configured, the mod does nothing.
// Everything Claude Code-specific lives here; code shared with the OpenCode adapter is in vsab-plugin-common.js.
// One tool.call handler covers "before" and "after": the code before next(e) runs before the permission prompt
// and the tool, the code after it once the tool has finished.
import {
    BRIDGE_TIMEOUT_MS, makeRequest as makeBridgeRequest, parseBridgeOutput,
    blockedText, commandBlockedText, notAddedText,
} from "./vsab-plugin-common.js"

// Claude Code tools that write the single file named by the given input field.
const WRITE_TOOLS = { Edit: "file_path", Write: "file_path", NotebookEdit: "notebook_path" }

// Claude Code tools that read the single file named by the given input field: sent to the session's copy if there is one.
const READ_TOOLS = { Read: "file_path" }

// Claude Code tools that run a shell command (e.command). Monitor runs one in the background (or opens a WebSocket).
const SHELL_TOOLS = ["Bash", "PowerShell", "Monitor"]

export function register(on, options) {
    const roots = parseRoots(options?.workspaceRoots)
    if (roots.length === 0) return // the bridge is off: no hooks at all

    // The matcher repeats the three lists above literally, so that "claude plugin validate" shows them.
    on("tool.call", { tool: ["Edit", "Write", "NotebookEdit", "Read", "Bash", "PowerShell", "Monitor"] },
        ($, e, next) => handle($, e, next, roots)).catch(failClosed)
}

async function handle($, e, next, roots) {
    if (e.tool in WRITE_TOOLS) return handleFile($, e, next, roots, WRITE_TOOLS[e.tool], false)
    if (e.tool in READ_TOOLS) return handleFile($, e, next, roots, READ_TOOLS[e.tool], true)
    return handleShell($, e, next, roots)
}

async function handleFile($, e, next, roots, field, isRead) {
    const filePath = e[field]
    if (typeof filePath !== "string" || filePath.trim() === "") return next(e) // the tool reports its own input errors
    if (!isUnderRoots(filePath, roots)) return next(e)

    const request = await makeRequest($, isRead ? "read" : "before", e.tool, filePath)
    const answer = await askBridge($, request)

    // A read changes nothing: if the bridge cannot run, the model reads the original rather than nothing.
    if (answer.failure !== undefined) return isRead ? next(e) : { deny: blockedText(answer.failure) }

    const response = answer.response
    if (response.decision === "deny") return { deny: response.message }

    if (response.decision === "redirect") {
        const result = await next({ ...e, [field]: response.path })
        return succeeded(result) ? withContext(result, response.message) : result
    }

    const result = await next(e)
    if (response.after !== true || !succeeded(result)) return result // nothing was written (refused, failed)

    const afterRequest = await makeRequest($, "after", e.tool, filePath)
    const afterAnswer = await askBridge($, afterRequest)
    const text = afterAnswer.failure !== undefined ? notAddedText(filePath, afterAnswer.failure) : afterAnswer.response.message
    return typeof text === "string" && text !== "" ? withContext(result, text) : result
}

// A shell command: refused when it deletes, moves or writes files. The bridge failing blocks every command
// (fail-closed, slice 4 results, §1.4). Claude Code's shell tools have no working-directory argument:
// the session's current directory decides whether the command is under a root, and is the command's workdir.
async function handleShell($, e, next, roots) {
    const command = e.command
    if (typeof command !== "string" || command.trim() === "") return next(e) // Monitor on a WebSocket, input errors

    const cwd = await $.session.cwd()
    if (!isUnderRoots(cwd, roots)) return next(e)

    const request = await makeRequest($, "shell", e.tool, undefined)
    request.command = command
    request.workdir = cwd

    const answer = await askBridge($, request)
    if (answer.failure !== undefined) return { deny: commandBlockedText(answer.failure) }
    if (answer.response.decision === "deny") return { deny: answer.response.message }
    if (answer.response.decision !== "allow")
        return { deny: commandBlockedText(`unexpected response '${answer.response.decision}'`) }
    return next(e)
}

// The handler threw or ran out of time. Before the tool ran: a read goes ahead, anything else is refused.
// After the tool ran (next was called): its result cannot be recovered here, so the model is told what happened.
async function failClosed($, e, next) {
    const reason = `${next.error.kind}: ${next.error.message}`
    if (next.called) {
        return {
            deny: `VS Agent Bridge failed after ${e.tool} ran (${reason}); the call may have changed files. ` +
                "At the end of your answer tell the user to check the file and the log of VS Agent Bridge."
        }
    }
    if (e.tool in READ_TOOLS) return next(e)
    if (SHELL_TOOLS.includes(e.tool)) return { deny: commandBlockedText(reason) }
    return { deny: blockedText(reason) }
}

async function makeRequest($, eventName, tool, filePath) {
    return makeBridgeRequest("claude", eventName, tool, filePath, await $.session.cwd(), await $.session.id())
}

// Runs "vsagentbridge.exe hook": { response } or { failure: "<reason>" }.
async function askBridge($, request) {
    const exe = exePath($)
    let run
    try {
        run = await $.process.run([exe, "hook"], { stdin: JSON.stringify(request), timeoutMs: BRIDGE_TIMEOUT_MS })
    } catch (err) {
        return { failure: `cannot run ${exe}: ${err?.message ?? err}` }
    }
    return parseBridgeOutput(run.exitCode, run.stdout, run.stderr)
}

// <output>\claude is the plugin folder; the exe is next to it.
function exePath($) {
    const root = $.plugin.root.replace(/[\\/]+$/, "")
    const parent = root.slice(0, Math.max(root.lastIndexOf("\\"), root.lastIndexOf("/")))
    return `${parent}\\vsagentbridge.exe`
}

// The tool ran without error and was not refused.
function succeeded(result) {
    return result !== undefined && result.deny === undefined && result.isError !== true
}

// Claude reads the context after the tool's result (one reminder); the user does not see it.
function withContext(result, text) {
    return { ...result, context: [...(result.context ?? []), text] }
}

// "D:\Projects; E:/Work/" → ["d:\projects", "e:\work"]: lower case, backslashes, no trailing separator.
function parseRoots(value) {
    if (typeof value !== "string") return []
    return value.split(";").map(normalizePath).filter((root) => root !== "")
}

function isUnderRoots(filePath, roots) {
    const p = normalizePath(filePath)
    return roots.some((root) => p === root || p.startsWith(root + "\\"))
}

function normalizePath(value) {
    return String(value).trim().replace(/\//g, "\\").replace(/\\+$/, "").toLowerCase()
}