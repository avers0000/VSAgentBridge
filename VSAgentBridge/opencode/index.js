// VS Agent Bridge — OpenCode adapter.
// Before OpenCode edits or writes a file, asks vsagentbridge.exe (one folder up) whether the edit may go ahead;
// the exe checks the file out through Visual Studio when needed.
// Everything OpenCode-specific lives here: the exe knows nothing about OpenCode, its tools or their inputs.
import { spawnSync } from "node:child_process"
import fs from "node:fs"
import path from "node:path"
import { fileURLToPath } from "node:url"

const pluginDir = path.dirname(fileURLToPath(import.meta.url))
const exePath = path.join(pluginDir, "..", "vsagentbridge.exe")

// OpenCode tools that write to the single file named by input.path.
// patch (several files inside patchText) comes later.
const FILE_TOOLS = new Set(["edit", "write"])

// Checkout may wait for a busy Visual Studio (E_PENDING retries, message filter): up to ~20 s in the worst case.
const TIMEOUT_MS = 30_000

export default {
    id: "vsagentbridge",
    setup: async (ctx) => {
        const cwd = ctx.location?.directory

        // Synchronous on purpose: OpenCode is known to cancel the call when a synchronous handler throws;
        // whether it waits for an async handler was never tested, and the edit must not start before the checkout.
        await ctx.tool.hook("execute.before", (event) => {
            if (!FILE_TOOLS.has(event.tool)) return

            const filePath = event.input?.path
            if (typeof filePath !== "string" || filePath.trim() === "") return // the tool reports its own input errors

            const request = { event: "before", harness: "opencode", tool: event.tool, path: filePath }
            if (cwd) request.cwd = cwd
            if (event.sessionID) request.session = event.sessionID

            const response = askBridge(request)
            if (response.decision === "deny") throw new Error(response.message)
        })
    },
}

// Runs "vsagentbridge.exe hook" and returns its response. Any failure of the bridge blocks the edit
// with an explicit message: a broken bridge must be visible, not silently skipped.
function askBridge(request) {
    if (!fs.existsSync(exePath)) bridgeFailed(`${exePath} not found`)

    const result = spawnSync(exePath, ["hook"], {
        input: Buffer.from(JSON.stringify(request), "utf8"),
        encoding: "utf8",
        timeout: TIMEOUT_MS,
        windowsHide: true,
    })

    if (result.error) {
        bridgeFailed(result.error.code === "ETIMEDOUT"
            ? `no answer within ${TIMEOUT_MS / 1000} s`
            : `cannot run ${exePath}: ${result.error.message}`)
    }
    if (result.signal) bridgeFailed(`vsagentbridge.exe was terminated (${result.signal})`)
    if (result.status !== 0) {
        const stderr = (result.stderr ?? "").trim()
        bridgeFailed(`vsagentbridge.exe exited with code ${result.status}${stderr ? ": " + stderr : ""}`)
    }

    let response
    try {
        response = JSON.parse(result.stdout)
    } catch {
        bridgeFailed(`unreadable response: ${shorten(result.stdout)}`)
    }

    if (response?.decision === "allow") return response
    if (response?.decision === "deny" && typeof response.message === "string" && response.message) return response
    bridgeFailed(`unexpected response: ${shorten(result.stdout)}`)
}

function bridgeFailed(reason) {
    throw new Error(
        `VS Agent Bridge is not working: ${reason}. The edit was not made. ` +
        "Do not retry this edit and do not write the change to another file or location. " +
        "Show the intended change in your reply as a diff, and at the end of your answer tell the user: " +
        `VS Agent Bridge failed (${reason}); check its installation and its log.`)
}

function shorten(text) {
    const s = String(text ?? "").trim()
    return s.length > 300 ? s.slice(0, 300) + "..." : s || "(empty)"
}