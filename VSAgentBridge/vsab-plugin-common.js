// VS Agent Bridge — code shared by the harness adapters (OpenCode plugin, Claude Code mod).
// Builds a request for "vsagentbridge.exe hook", checks the exe's answer, and holds the texts the model gets
// when the bridge itself could not run. Starting the exe is the adapter's job: each harness has its own way.
// Plain JavaScript with no imports: the Claude Code mod runs without Node, OpenCode runs it under Node.

// Checkout or adding to a project may wait for a busy Visual Studio (E_PENDING retries, message filter).
export const BRIDGE_TIMEOUT_MS = 30_000

// One request in the bridge's own, harness-neutral format.
export function makeRequest(harness, eventName, tool, filePath, cwd, session) {
    const request = { event: eventName, harness, tool, path: filePath }
    if (cwd) request.cwd = cwd
    if (session) request.session = session
    return request
}

// Checks what the exe printed after it ran (it was started and exited by itself).
// Returns { response } for a valid answer, or { failure: "<reason>" } for anything else;
// what a failure means (block the call, go on, add a note) is decided by the adapter.
export function parseBridgeOutput(exitCode, stdout, stderr) {
    if (exitCode !== 0) {
        const err = String(stderr ?? "").trim()
        return { failure: `vsagentbridge.exe exited with code ${exitCode}${err ? ": " + err : ""}` }
    }

    let response
    try {
        response = JSON.parse(stdout)
    } catch {
        return { failure: `unreadable response: ${shorten(stdout)}` }
    }

    if (response?.decision === "allow") return { response }
    if (response?.decision === "deny" && nonEmpty(response.message)) return { response }
    if (response?.decision === "redirect" && nonEmpty(response.path) && nonEmpty(response.message)) return { response }
    return { failure: `unexpected response: ${shorten(stdout)}` }
}

export function blockedText(reason) {
    return `VS Agent Bridge blocked this edit: the bridge could not run (${reason}). The file was not changed. ` +
        "Do not retry this change with any tool (edit, write or any other) and do not write it to another file or location. " +
        "Show the intended change in your reply as a diff, and at the end of your answer tell the user: " +
        `VS Agent Bridge could not run (${reason}); check its installation and its log, then ask to repeat the change.`
}

export function commandBlockedText(reason) {
    return `VS Agent Bridge blocked this command: the bridge could not run (${reason}). Nothing was run. ` +
        "Do not retry it with any tool. At the end of your answer tell the user: " +
        `VS Agent Bridge could not run (${reason}); check its installation and its log.`
}

export function patchBlockedText(reason) {
    return `VS Agent Bridge blocked this patch: the bridge could not run (${reason}). Nothing in the patch was applied. ` +
        "Do not retry this change with any tool and do not write it to another file or location. " +
        "Show the intended change in your reply as a diff, and at the end of your answer tell the user: " +
        `VS Agent Bridge could not run (${reason}); check its installation and its log, then ask to repeat the change.`
}

export function notAddedText(filePath, reason) {
    return `VS Agent Bridge: ${filePath} was created, but it was not added to a project: the bridge could not run (${reason}). ` +
        "It is in no project and not in TFVC. Do not create it again and do not edit project files to include it. " +
        "At the end of your answer tell the user: the new file was not added to a project; add it in Visual Studio " +
        "(right-click its folder in Solution Explorer, Add > Existing Item), and check the installation and the log of VS Agent Bridge."
}

export function nonEmpty(value) {
    return typeof value === "string" && value !== ""
}

export function shorten(text) {
    const s = String(text ?? "").trim()
    return s.length > 300 ? s.slice(0, 300) + "..." : s || "(empty)"
}