# VS Agent Bridge

A bridge between coding agents (OpenCode, Claude Code) and Visual Studio:
file edits made by an agent follow the rules of the open VS solution and
its version control (currently TFVC server workspaces).

Status: early development (slice 0 — skeleton and `diag` mode).

- Target: .NET Framework 4.8, console app `vsagentbridge.exe`
- Supported Visual Studio: 2019 / 2022 / 2026 (via DTE / COM)