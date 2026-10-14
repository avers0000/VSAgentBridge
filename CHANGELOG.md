# VS Agent Bridge - Changelog
All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-14
### Added
- Initial release.
- Plugins for Claude Code CLI (a mod, installed from a local marketplace) and OpenCode that pass the agent's file
  edits, new files and shell commands under the workspace roots to `vsagentbridge.exe`.
- Through the Visual Studio instance that has the solution open: TFVC checkout before an edit, new files added to
  their Visual Studio projects, changes that cannot be made in place redirected to a copy in `Output`, deleting,
  moving and renaming refused (also in shell commands), BOM, line endings and final newline of rewritten files
  restored.
- MCP tool `create_file` for creating a file and adding it to its project in one step.
- `vsagentbridge init claude|opencode`: interactive setup of an agent, with copies of the changed settings files.
- Rules for the agent: `AGENTS.global.sample.md` (global) and `AGENTS.sample.md` (per folder).
