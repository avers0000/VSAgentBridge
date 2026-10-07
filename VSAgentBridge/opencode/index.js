// VS Agent Bridge — OpenCode adapter.
// Before OpenCode edits, writes or reads a file, asks vsagentbridge.exe (one folder up) what to do with the call:
// let it through (allow), block it (deny), or send it to another path (redirect: a copy under the bridge's Output).
// When the exe asks for it ("after": true), calls it again once the tool has completed, so that a new file is added
// to its project; the exe's message is appended to the tool's result. A redirect's message is appended the same way.
// Before a shell command runs, and before a patch deletes or moves files, asks the exe too: commands that delete,
// move or write files and patches that delete or move them are refused (slice 4); a command is never changed.
// Everything OpenCode-specific lives here: the exe knows nothing about OpenCode, its tools or their inputs.
import { spawnSync } from "node:child_process"
import fs from "node:fs"
import path from "node:path"
import { fileURLToPath } from "node:url"

const pluginDir = path.dirname(fileURLToPath(import.meta.url))
const exePath = path.join(pluginDir, "..", "vsagentbridge.exe")

// OpenCode tools that write to the single file named by input.path.
// patch (several files inside patchText) comes later.
const WRITE_TOOLS = new Set(["edit", "write"])

// OpenCode tools that read the single file named by input.path: sent to the session's copy if there is one.
const READ_TOOLS = new Set(["read"])

// OpenCode tools that run a shell command (input.command, optional input.workdir); "bash" is the older name.
const SHELL_TOOLS = new Set(["shell", "bash"])

// OpenCode tools that apply a patch (input.patchText); only its deleted and moved files are checked for now.
const PATCH_TOOLS = new Set(["patch", "apply_patch"])

// Checkout or adding to a project may wait for a busy Visual Studio (E_PENDING retries, message filter).
const TIMEOUT_MS = 30_000

// Work left for "after": either a call to the bridge (the exe asked for it) or a message to append (a redirect).
// An entry whose "after" never comes (tool cancelled, OpenCode quirk) is dropped after this time.
const PENDING_MAX_AGE_MS = 10 * 60_000
const pending = []

export default {
    id: "vsagentbridge",
    setup: async (ctx) => {
        const cwd = ctx.location?.directory

        // Synchronous on purpose: OpenCode is known to cancel the call when a synchronous handler throws;
        // whether it waits for an async handler was never tested, and the edit must not start before the checkout.
        await ctx.tool.hook("execute.before", (event) => {
            if (SHELL_TOOLS.has(event.tool)) return checkShell(event, cwd)
            if (PATCH_TOOLS.has(event.tool)) return checkPatch(event, cwd)

            const isWrite = WRITE_TOOLS.has(event.tool)
            const isRead = READ_TOOLS.has(event.tool)
            if (!isWrite && !isRead) return

            const filePath = event.input?.path
            if (typeof filePath !== "string" || filePath.trim() === "") return // the tool reports its own input errors

            const request = makeRequest(isRead ? "read" : "before", event.tool, filePath, cwd, event.sessionID)

            // A read changes nothing: if the bridge cannot run, the model reads the original rather than nothing.
            const response = isRead
                ? askBridge(request, () => undefined, { soft: true, silent: true })
                : askBridge(request, blockedText)
            if (!response) return

            if (response.decision === "deny") throw new Error(response.message)

            if (response.decision === "redirect") {
                event.input.path = response.path
                remember(event, filePath, response.path, { message: response.message })
                return
            }

            if (response.after === true) remember(event, filePath, filePath, { callAfter: true })
        })

        // Synchronous as well: the message must be in the result before OpenCode hands it to the model.
        await ctx.tool.hook("execute.after", (event) => {
            if (!WRITE_TOOLS.has(event.tool) && !READ_TOOLS.has(event.tool)) return

            const entry = takePending(event)
            if (!entry) return
            if (event.status !== "completed") return // nothing was written or read: nothing to add

            if (entry.message) {
                appendToResult(event, entry.message)
                return
            }

            const request = makeRequest("after", entry.tool, entry.path, cwd, entry.session)
            const response = askBridge(request, (reason) => notAddedText(entry.path, reason), { soft: true })
            if (typeof response.message === "string" && response.message) appendToResult(event, response.message)
        })
    },
}

// A shell command: refused when it deletes, moves or writes files. The bridge failing blocks every command
// (fail-closed, slice 4 results, §1.4): without the bridge the agent must not change files at all.
function checkShell(event, cwd) {
    const command = event.input?.command
    if (typeof command !== "string" || command.trim() === "") return // the tool reports its own input errors

    const request = makeRequest("shell", event.tool, undefined, cwd, event.sessionID)
    request.command = command
    const workdir = event.input?.workdir
    if (typeof workdir === "string" && workdir !== "") request.workdir = workdir

    const response = askBridge(request, commandBlockedText)
    if (response.decision === "deny") throw new Error(response.message)
    if (response.decision !== "allow") throw new Error(commandBlockedText(`unexpected response '${response.decision}'`))
}

// A patch applies all or nothing: one deleted or moved file refuses the whole patch. Every such file is sent,
// so that the bridge records all of them; the model gets the first refusal.
function checkPatch(event, cwd) {
    const patchText = event.input?.patchText
    if (typeof patchText !== "string") return

    let refusal
    for (const op of patchMoves(patchText)) {
        const request = makeRequest(op.newPath ? "rename" : "delete", event.tool, op.path, cwd, event.sessionID)
        if (op.newPath) request.newPath = op.newPath

        const response = askBridge(request, patchBlockedText)
        if (response.decision !== "allow" && refusal === undefined)
            refusal = response.message || patchBlockedText(`unexpected response '${response.decision}'`)
    }
    if (refusal !== undefined) throw new Error(refusal)
}

// "*** Delete File: a" → { path: "a" }; "*** Update File: a" followed by "*** Move to: b" → { path: "a", newPath: "b" }.
// Only header lines count: content lines start with "+", "-" or " ".
function patchMoves(patchText) {
    const ops = []
    let updated
    for (const raw of patchText.split(/\r?\n/)) {
        const line = raw.trimEnd()
        let m
        if ((m = /^\*\*\* Delete File:\s*(.+)$/.exec(line))) {
            ops.push({ path: m[1].trim() })
            updated = undefined
        } else if ((m = /^\*\*\* Update File:\s*(.+)$/.exec(line))) {
            updated = m[1].trim()
        } else if ((m = /^\*\*\* Move to:\s*(.+)$/.exec(line)) && updated !== undefined) {
            ops.push({ path: updated, newPath: m[1].trim() })
            updated = undefined
        } else if (line.startsWith("*** ")) {
            updated = undefined
        }
    }
    return ops
}

function makeRequest(eventName, tool, filePath, cwd, session) {
    const request = { event: eventName, harness: "opencode", tool, path: filePath }
    if (cwd) request.cwd = cwd
    if (session) request.session = session
    return request
}

// path: the path the model gave (what the bridge is asked about again in "after");
// inputPath: the path the tool actually runs with (differs after a redirect), for matching the "after" event.
function remember(event, filePath, inputPath, work) {
    dropStale()
    pending.push({
        tool: event.tool,
        path: filePath,
        inputPath,
        session: event.sessionID,
        callID: event.callID,
        since: Date.now(),
        ...work,
    })
}

// Finds and removes the "before" entry this "after" belongs to. Best match first:
// the call id, then session + path from the tool input, then the oldest entry of the same tool and session.
function takePending(event) {
    let index = -1
    if (event.callID) index = pending.findIndex((p) => p.callID === event.callID)
    if (index < 0) {
        const input = event.input ?? event.args
        const filePath = input?.path
        if (typeof filePath === "string")
            index = pending.findIndex((p) => p.tool === event.tool && (p.inputPath === filePath || p.path === filePath)
                && sameSession(p, event))
    }
    if (index < 0) index = pending.findIndex((p) => p.tool === event.tool && sameSession(p, event))
    return index < 0 ? undefined : pending.splice(index, 1)[0]
}

function sameSession(entry, event) {
    return !entry.session || !event.sessionID || entry.session === event.sessionID
}

function dropStale() {
    const now = Date.now()
    for (let i = pending.length - 1; i >= 0; i--) if (now - pending[i].since > PENDING_MAX_AGE_MS) pending.splice(i, 1)
}

// The result is a string or an object whose "content" (string or array of parts) is what the model sees.
// A new value is always assigned to event.result: OpenCode takes the reassigned value (as in its own examples);
// changing the old object in place may be lost, and its arrays may be frozen.
function appendToResult(event, text) {
    const result = event.result
    if (typeof result === "string") {
        event.result = `${result}\n\n${text}`
        return
    }

    const base = result ?? {}
    const content = Array.isArray(base.content) ? [...base.content, { type: "text", text }]
        : typeof base.content === "string" ? `${base.content}\n\n${text}`
            : [{ type: "text", text }]
    event.result = { ...base, content }
}

// Runs "vsagentbridge.exe hook" and returns its response.
// A failure of the bridge is never silent for a write: "before" blocks the call (throws); "after" (soft) returns
// a response whose message tells the model the file was not added — the file already exists then.
// A read (soft + silent) just goes ahead: the response is undefined and the model reads the original.
function askBridge(request, failureText, { soft = false, silent = false } = {}) {
    const fail = (reason) => {
        if (silent) return undefined
        if (soft) return { decision: "allow", reason: "BridgeFailed", message: failureText(reason) }
        throw new Error(failureText(reason))
    }

    if (!fs.existsSync(exePath)) return fail(`${exePath} not found`)

    const result = spawnSync(exePath, ["hook"], {
        input: Buffer.from(JSON.stringify(request), "utf8"),
        encoding: "utf8",
        timeout: TIMEOUT_MS,
        windowsHide: true,
    })

    if (result.error) {
        return fail(result.error.code === "ETIMEDOUT"
            ? `no answer within ${TIMEOUT_MS / 1000} s`
            : `cannot run ${exePath}: ${result.error.message}`)
    }
    if (result.signal) return fail(`vsagentbridge.exe was terminated (${result.signal})`)
    if (result.status !== 0) {
        const stderr = (result.stderr ?? "").trim()
        return fail(`vsagentbridge.exe exited with code ${result.status}${stderr ? ": " + stderr : ""}`)
    }

    let response
    try {
        response = JSON.parse(result.stdout)
    } catch {
        return fail(`unreadable response: ${shorten(result.stdout)}`)
    }

    if (response?.decision === "allow") return response
    if (response?.decision === "deny" && nonEmpty(response.message)) return response
    if (response?.decision === "redirect" && nonEmpty(response.path) && nonEmpty(response.message)) return response
    return fail(`unexpected response: ${shorten(result.stdout)}`)
}

function nonEmpty(value) {
    return typeof value === "string" && value !== ""
}

function blockedText(reason) {
    return `VS Agent Bridge blocked this edit: the bridge could not run (${reason}). The file was not changed. ` +
        "Do not retry this change with any tool (edit, write or any other) and do not write it to another file or location. " +
        "Show the intended change in your reply as a diff, and at the end of your answer tell the user: " +
        `VS Agent Bridge could not run (${reason}); check its installation and its log, then ask to repeat the change.`
}

function commandBlockedText(reason) {
    return `VS Agent Bridge blocked this command: the bridge could not run (${reason}). Nothing was run. ` +
        "Do not retry it with any tool. At the end of your answer tell the user: " +
        `VS Agent Bridge could not run (${reason}); check its installation and its log.`
}

function patchBlockedText(reason) {
    return `VS Agent Bridge blocked this patch: the bridge could not run (${reason}). Nothing in the patch was applied. ` +
        "Do not retry this change with any tool and do not write it to another file or location. " +
        "Show the intended change in your reply as a diff, and at the end of your answer tell the user: " +
        `VS Agent Bridge could not run (${reason}); check its installation and its log, then ask to repeat the change.`
}

function notAddedText(filePath, reason) {
    return `VS Agent Bridge: ${filePath} was created, but it was not added to a project: the bridge could not run (${reason}). ` +
        "It is in no project and not in TFVC. Do not create it again and do not edit project files to include it. " +
        "At the end of your answer tell the user: the new file was not added to a project; add it in Visual Studio " +
        "(right-click its folder in Solution Explorer, Add > Existing Item), and check the installation and the log of VS Agent Bridge."
}

function shorten(text) {
    const s = String(text ?? "").trim()
    return s.length > 300 ? s.slice(0, 300) + "..." : s || "(empty)"
}