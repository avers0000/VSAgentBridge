# VS Agent Bridge

**VS Agent Bridge connects CLI coding agents - Claude Code CLI and OpenCode - to Visual Studio 2019, 2022 and 2026
(older versions may work but are not tested).**
When an agent edits, creates or deletes files in your solution, the bridge makes it happen the way Visual Studio
would: files under TFVC are checked out first, new files are added to their Visual Studio projects, and what cannot be
done safely is redirected or refused instead of silently breaking the solution.

- [What it does](#what-it-does)
- [Requirements](#requirements)
- [Quick start](#quick-start)
- [What init changes](#what-init-changes)
  - [Backups of your settings](#backups-of-your-settings)
  - [Claude Code](#claude-code)
  - [OpenCode](#opencode)
- [Rules for the agent](#rules-for-the-agent)
- [Turning the bridge off for a folder](#turning-the-bridge-off-for-a-folder)
- [Configuration](#configuration)
- [Elevation level](#elevation-level)
- [Limitations](#limitations)
- [Uninstalling](#uninstalling)
- [License](#license)
- [Contributions](#contributions)

## What it does

Agents edit files with their own tools and know nothing about Visual Studio projects or TFVC. The bridge hooks into
those tools and asks the Visual Studio instance that has the solution open:

- **Edits of files under TFVC** - a read-only file is checked out through Visual Studio before the agent writes it,
  so the change appears in Pending Changes as a normal edit.
- **New files** - added to their Visual Studio project (including new folders), with the line endings and encoding of
  their neighbours. The agent gets a `create_file` tool for this; files written with the agent's own write tool are
  added as well.
- **When that is not possible** (the solution is not open, the file is not in it, Visual Studio does not answer, ...) -
  the change goes to a copy in the bridge's `Output` folder, the original stays untouched, and the agent tells you where
  the copy is and what to do with it.
- **Deleting, moving and renaming files** - refused, including through shell commands. The agent lists what should be
  deleted or renamed, and you do it in Visual Studio, so projects and version control stay consistent.
- **File format** - when an agent rewrites an existing file, its BOM, line endings and final newline are restored.

### Several layers of protection

The layers back each other up: the agent does not have to get everything right for the solution to stay consistent.

1. **Rules for the agent** (`CLAUDE.md` / `AGENTS.md`) tell the model how to work in a Visual Studio solution, so it
   usually does the right thing by itself.
2. **The `create_file` tool** (an MCP server that comes with the plugins) creates a new file and adds it to its
   project in one step.
3. **Hooks on the agent's own tools** catch everything else. A new file written with the agent's ordinary write
   tool is still added to its project, an edit of a read-only file is still checked out, and a delete in a shell
   command is still refused - even if the model ignores the rules or the `create_file` tool is not available.
4. **Fail-closed.** If the bridge itself cannot answer (it crashed or did not start), the file change or command is
   blocked rather than let through unchecked.

The bridge works only in the folders you configure (workspace roots); elsewhere the agent works as usual.
It works with any version control or none. With TFVC server workspaces it adds a checkout before an edit; local
workspaces keep files writable and need no checkout.

## Requirements

- Windows 10 or 11 with .NET Framework 4.8 (part of current Windows).
- Visual Studio 2019, 2022 or 2026 (older versions may work but are not tested) with the solution open.
- One or both agents:
  - **Claude Code CLI 2.1.287 or later** (the bridge plugin for Claude Code is a mod; mods need this version);
  - **OpenCode 2.0 or later** (configuration format 2.x; tested with 2.0.6).

## Quick start

> **Placeholders.** `<version>` is the version in the name of the downloaded file (for example `1.0.0`).
> `<install-dir>` is the folder you extract the package to (step 2) - replace it with your path everywhere below.

1. Download `VSAgentBridge-<version>.zip` and `VSAgentBridge-<version>.zip.sha256` from the Releases page.
   Optionally check that the download is intact: open PowerShell in the folder with both files (in Explorer:
   right-click the folder > **Open in Terminal**), then run:

```powershell
Get-ChildItem .\VSAgentBridge-*.zip.sha256 | ForEach-Object { $hash, $zip = (Get-Content $_) -split '\s+'; "$zip $((Get-FileHash $zip -Algorithm SHA256).Hash -eq $hash)" }
```

   `True` after the file name means the file is the one that was published.

2. Extract it to a folder of your choice, for example `C:\Tools\VSAgentBridge` - this is `<install-dir>`.
   The bridge writes its `Logs`, `Output` and `Settings` folders there, so choose a folder you can write to without
   administrator rights - not `Program Files` - and preferably one that is not synced to the cloud (OneDrive and the
   like). Keep the layout as it is: the plugins find `vsagentbridge.exe` one folder up from their own folder.

```
<install-dir>\
  vsagentbridge.exe (+ DLLs, vsagentbridge.exe.config, vsagentbridge.sample.json)
  AGENTS.global.sample.md, AGENTS.sample.md
  claude\      plugin (mod) for Claude Code
  opencode\    plugin for OpenCode
  Output\      changes that could not be made in place
```

3. Open PowerShell in `<install-dir>` and set up each agent you use, one command per agent:

```powershell
.\vsagentbridge.exe init claude
.\vsagentbridge.exe init opencode
```

   `init` first checks that the agent is installed and recent enough (otherwise it stops without changes), then asks:

   - **Workspace roots** - the folders where the bridge works, usually the folders that contain your solutions.
     Absolute paths of existing folders, separated by `;`, for example `D:\Projects;D:\Work\Solutions`.
     For Claude Code the current roots are shown in brackets; press Enter to keep them.
   - **Install the rules globally (<file>)? [Y/n]** - the [rules for the agent](#rules-for-the-agent). Yes is
     recommended.

   Then it lists every change it is going to make and asks **Apply? [Y/n]**. Nothing is changed before you answer
   yes. At the end it says what was done and where it saved copies of your settings files from before and after the
   change - to restore a file if something goes wrong (see [Backups of your settings](#backups-of-your-settings)).

4. Only for OpenCode: restart its service from a terminal with the same [elevation level](#elevation-level) as Visual
   Studio:

```powershell
opencode service restart
```

5. **Start the agent in your solution's folder - a folder under one of the workspace roots**, from a terminal with the
   same [elevation level](#elevation-level) as Visual Studio. The agent's start folder decides a lot: the rules check
   it and apply only under the workspace roots, and OpenCode loads the bridge only from the `opencode.json` in a
   workspace root, so started elsewhere it runs without the bridge. The simplest way is Visual Studio's own terminal
   (View > Terminal): it opens in the solution's folder with Visual Studio's elevation level.

   Check that the bridge is on:

   - **Claude Code** - in a new session, `/plugin` shows `1 mod active · vsagentbridge`.
   - **OpenCode** - the tool `vsab_create_file` is available (see the [check for OpenCode](#opencode)).

**Answers on the command line.** Instead of answering the questions you can pass the answers:

```powershell
.\vsagentbridge.exe init claude --roots "D:\Projects;D:\Work\Solutions" -g
```

| Option | Meaning |
|-|-|
| `--roots "<folder>;<folder>"` | workspace roots. Keep the quotes: in PowerShell `;` separates commands. |
| `-g` (or `--global`) | install the rules globally without asking |
| `--no-marketplace` | Claude Code only: [install the plugin without the marketplace](#alternative-without-the-marketplace) |

**Running `init` again** is safe and is the way to change the setup: other workspace roots, another agent, a new
version extracted to another folder, or `<install-dir>` moved. It changes only its own entries and never deletes
files. `init claude` always runs Claude Code's plugin commands again (they change nothing if all is set);
`init opencode` says *Everything is up to date; nothing to change.* when there is nothing to do.

Exit codes: `0` - done, nothing to change, or *no* on *Apply?*; `1` - an error or stopped; `2` - wrong options.

## What init changes

> **For advanced users.** If `init` worked for you, you do not need this section. Read it to check what `init` did,
> to adjust or undo a setting by hand, or to set the bridge up without `init` (for example, when your organization
> restricts something `init` needs).

`init` does only what you could do by hand. This section lists every change. Change these entries by hand if you
want; a later `init` sets them again to its values, and leaves everything else in the files as it is.

### Backups of your settings

`init` changes your agents' settings and rules files. Before the first change of such a file, and at every run that
applies changes, it keeps copies of them in `<install-dir>\Settings`, so you can see what was changed and restore a
file:

```
<install-dir>\Settings\
  claude\     init\                 files as they were before the bridge changed them for the first time
              <date-time>\before\   files before this run
              <date-time>\after\    files after this run
  opencode\   (the same)
```

Each copy is in a subfolder that repeats the file's full path, for example
`Settings\claude\init\C\Users\<you>\.claude\settings.json` (OpenCode has an `opencode.json` in each workspace root,
so their copies need different paths). A file that did not exist before has no copy in `init\` and `before\`.

To restore a file, copy it back from `before\` (undo one run) or from `init\` (the state before the bridge) to its
original place. Do this only if you have not changed the file since, or compare the files first: a later change by
you or by the agent would be lost. To remove the Claude Code plugin, use the commands in [Uninstalling](#uninstalling)
rather than an old `settings.json`: Claude Code keeps records of plugins and marketplaces outside this file as well.

Do not delete the `Settings` folder: `init\` is written only once, at the first run.

- `init` reads JSON files with comments and trailing commas, but writes them back without comments (indent 2, the
  order of keys is kept). If a file had comments, `init` warns; the original is in the backups.
- `settings.json` may contain secrets (for example in `env`); the copies are as private as the `<install-dir>` folder.

### Claude Code

Files: `%USERPROFILE%\.claude\settings.json` and, for the rules, `%USERPROFILE%\.claude\CLAUDE.md` (if
`CLAUDE_CONFIG_DIR` is set, both are in that folder instead).

The plugin for Claude Code is in `<install-dir>\claude`. Claude Code installs plugins from *marketplaces* - catalogs of
plugins. The `claude` folder is such a catalog itself, named `vsab`, with one plugin in it, `vsagentbridge`; the plugin
id is `vsagentbridge@vsab`. Nothing is downloaded or copied: Claude Code loads the plugin from
`<install-dir>\claude`, next to `vsagentbridge.exe`.

| What | How | Where it ends up in `settings.json` |
|-|-|-|
| Register the catalog | `claude plugin marketplace add "<install-dir>\claude"` | `extraKnownMarketplaces.vsab` |
| Install the plugin with the workspace roots | `claude plugin install vsagentbridge@vsab --scope user --config "workspaceRoots=<roots>"` | `enabledPlugins."vsagentbridge@vsab"`, `pluginConfigs."vsagentbridge@vsab".options.workspaceRoots` |
| Record the plugin version | `claude plugin update vsagentbridge@vsab` | Claude Code's own record (the version shown in `/plugin`) |
| Let the agent work in `Output` without asking | written by `init`; the folder is created if missing | `permissions.additionalDirectories` |
| Rules | written by `init` (if chosen) | not in `settings.json`: `CLAUDE.md` next to it, see [rules](#rules-for-the-agent) |

- If `vsab` already points to another folder (an older `<install-dir>`), `init` shows the old and the new path before
  *Apply?*; the `marketplace add` command moves `vsab` to the new folder.
- `Output` must exist when Claude Code starts; otherwise Claude Code skips it for the whole session. When you clean
  `Output`, delete its contents, not the folder.
- To change the workspace roots inside Claude Code: `/plugin configure vsagentbridge`. An empty value turns the
  bridge off.
- Check: `/plugin` shows `1 mod active · vsagentbridge`, and `/mcp` shows the server `plugin:vsagentbridge:vsab`.
- **Permission mode.** *Accept edits* works well with the bridge: the bridge acts before Claude Code asks for
  permission, so checkouts, redirects and refusals happen in any mode.

**If the marketplace cannot be used.** If `marketplace add` or `plugin install` fails (for example, an organization's
managed settings may restrict marketplaces), `init` shows the command's output and asks what to do:

1. use the alternative way below, plus all other settings;
2. apply all other settings only, and set up the plugin by hand (the commands in the table above, or the alternative
   way below);
3. stop without further changes.

#### Alternative without the marketplace

Not recommended: Claude Code documents this way for plugin development, and an organization's managed settings can turn
it off as well. Use it only if the marketplace way does not work for you. `init` uses it with `--no-marketplace` or
after choice 1 above, and writes to `settings.json`:

```json
"env": {
  "CLAUDE_CODE_PLUGIN_DIRS": "<install-dir>\\claude"
},
"pluginConfigs": {
  "vsagentbridge@inline": { "options": { "workspaceRoots": "D:\\Projects;D:\\Work\\Solutions" } }
}
```

plus `Output` and the rules, as above. The plugin id is then `vsagentbridge@inline`; to change the workspace roots,
edit this entry or run `init` again. Use only one of the two ways: a plugin loaded through `CLAUDE_CODE_PLUGIN_DIRS`
silently replaces the installed one (`init` warns about it). If you switch to this way, remove the installed plugin:
`claude plugin uninstall vsagentbridge@vsab`.

### OpenCode

OpenCode has no user-wide plugin settings with workspace roots, so `init` puts an `opencode.json` into **each**
workspace root (OpenCode reads it for sessions started in that folder or below). An existing file is completed:

```json
{
  "plugins": ["<install-dir>\\opencode"],
  "mcp": {
    "servers": {
      "vsab": {
        "type": "local",
        "command": ["<install-dir>\\vsagentbridge.exe", "mcp"],
        "codemode": false
      }
    }
  },
  "permission": {
    "external_directory": { "<install-dir with forward slashes>/Output/*": "allow" },
    "vsab_create_file": "ask"
  }
}
```

- `plugins` - the plugin folder; an entry for an older `<install-dir>` is replaced, so the plugin is not loaded twice.
- `mcp.servers.vsab` - the `create_file` tool. `type` and `command` are always set; `codemode` only if it is not set.
  `"codemode": false` makes `vsab_create_file` a regular tool; with code mode on, the tool is reachable only through
  OpenCode's `execute`, and many models do not find it. This is the 2.x format; the older `"mcp": { "vsab": ... }` is
  migrated by OpenCode and loses `codemode`.
- `external_directory` - lets the agent work with copies in `Output` without asking.
- `vsab_create_file` - only if it is not set.
- Rules (if chosen): `%USERPROFILE%\.config\opencode\AGENTS.md`, see [rules](#rules-for-the-agent).

Notes:

- Put workspace roots above your solution folders, not into them: `opencode.json` in a solution folder may end up in
  version control.
- If a folder has an `opencode.jsonc`, `init` warns: it writes `opencode.json` only.
- OpenCode's workspace roots are not remembered: at every run enter all of them. `opencode.json` in folders you no
  longer list is not deleted - remove the bridge entries there by hand.
- `init` does not restart the OpenCode service. After any change of `opencode.json`, restart it yourself from a
  terminal with the same [elevation level](#elevation-level) as Visual Studio: `opencode service restart`.
- **Edit permission.** For a large task with many files, *Always allow* on the `edit` question saves a lot of clicks.
  Note that OpenCode stores it permanently for the whole project (any file, all later sessions), and its TUI has no
  button to take it back. If you want to see every change, answer *Allow once*.
- Check: the OpenCode log (`%USERPROFILE%\.local\share\opencode\log\opencode.log`) shows the MCP server `vsab`
  connected with one tool.

## Rules for the agent

The rules are a required part of the setup. They tell the model how to work in a Visual Studio solution, so that
the bridge rarely has to step in (see [Several layers of protection](#several-layers-of-protection)).

> **`init` installs the rules for you** when you answer *yes* to *Install the rules globally?* (or pass `-g`) - the
> recommended way. Then you have nothing to do here; read on only to choose the per-folder way, to add your own
> project rules, or to understand what `init` wrote.

The package has two samples with the same rules. Install **one** of them, not both (the rules would be duplicated).

| | Globally - `AGENTS.global.sample.md` | Per folder - `AGENTS.sample.md` |
|-|-|-|
| Where | `%USERPROFILE%\.claude\CLAUDE.md` (Claude Code), `%USERPROFILE%\.config\opencode\AGENTS.md` (OpenCode) | `CLAUDE.md` (Claude Code) / `AGENTS.md` (OpenCode) in the working folder of a solution |
| Covers | every solution under the workspace roots, including new ones | only that folder |
| Context cost | about 700 tokens in **every** session, also outside the workspace roots (the model reads the condition and ignores the rest; cached) | only in sessions started in that folder |
| Setup | `init` (answer yes, or `-g`) | by hand, in each solution folder |
| Other | - | the file lies in the solution folder and may end up in version control if you do not exclude it |

**Globally.** `init` writes the rules with your workspace roots in the condition at the top, between the lines
`<!-- vsab:begin -->` and `<!-- vsab:end -->`. If the file has no such block, the rules are appended at the end; if it
has one, its content is replaced (for example with new roots). Everything outside the block stays as it is - keep your
own text there, edits inside the block are overwritten. If the file has a marker without its pair or several blocks,
`init` does not touch it: it saves the new rules as `vsab-rules.md` in the run's folder under `<install-dir>\Settings`
and asks you to copy them in by hand. By hand: copy `AGENTS.global.sample.md` with its markers to the end
of the file and replace the line `  - <folder with VS Agent Bridge>` with one such line per workspace root.

**Per folder.** Answer *no* to the rules question; `init` then only explains what to copy. Copy `AGENTS.sample.md` into
the folder you start the agent in - as `CLAUDE.md` for Claude Code, as `AGENTS.md` for OpenCode (if you use both
agents, make both copies). If the target file already exists, append the rules to it. Claude Code also reads
`CLAUDE.md` from parent folders; OpenCode reads `AGENTS.md` only from the folder it is started in.

OpenCode reads only `AGENTS.md` (not `CLAUDE.md`) and picks up changes without a restart.

**Project additions and overrides.** Write project-specific rules into the project's `CLAUDE.md` (Claude Code) or
`AGENTS.md` (OpenCode). Neither agent can replace a rule - global and project texts are simply joined, project text
last - so override a bridge rule explicitly:

> This project overrides the VS Agent Bridge rule "...": ...

With the per-folder setup you can also change the rules directly in that folder's file.

## Turning the bridge off for a folder

For a folder under a workspace root that has nothing to do with Visual Studio (documentation, scripts, ...).
In both agents this works by the folder the agent is **started** in.

- **Claude Code** - in that folder create `.claude\settings.json`:

```json
{ "enabledPlugins": { "vsagentbridge@vsab": false, "vsagentbridge@inline": false } }
```

  and answer *trust this folder* when Claude Code asks (without trust, project settings are not applied).
  This turns off the hooks and the `create_file` tool.

- **OpenCode** - in that folder create `opencode.json`:

```json
{
  "plugins": ["-vsagentbridge"],
  "mcp": { "servers": { "vsab": { "type": "local", "command": ["<install-dir>\\vsagentbridge.exe", "mcp"], "disabled": true } } }
}
```

  then restart the service. `"plugins": []` does not work (lists are merged); the server entry is replaced as a
  whole, so it needs `type` and `command` as well. This syntax comes from OpenCode's source, not from its
  documentation, and may change.

## Configuration

Optional. Without a configuration file the defaults apply. To change them, copy `vsagentbridge.sample.json` to
`vsagentbridge.json` next to `vsagentbridge.exe` and edit it (comments are allowed):

- `logLevel` - `off`, `issues`, `actions` (default) or `all`. The log is in
  `<install-dir>\Logs\vsagentbridge-yyyyMMdd.log`.
- `logRetentionDays` - daily logs older than this are deleted; `0` keeps them (default `30`).
- `outputDir` - absolute path of the `Output` folder (default: `Output` next to the exe). If you change it, run `init`
  again for each agent: it creates the new folder and allows it (`additionalDirectories`, `external_directory`).
  The entries for the old folder are not removed.

A broken file or value does not stop the bridge: defaults are used for what could not be read, with a warning in
the log.

Changes apply to file edits at once - the hooks read the file on every call. The MCP server (`vsab_create_file`)
reads it when it starts: start a new Claude Code session, or restart the OpenCode service.

## Elevation level

The agent must run with **the same elevation level as Visual Studio**. Windows does not let a non-elevated process
talk to an elevated Visual Studio, and the bridge then sees no Visual Studio at all (the log shows `vsInstances=0`, and
every change goes to `Output`).

- If you run Visual Studio as administrator, start the agent from an elevated terminal - for example Visual Studio's
  own terminal (View > Terminal), which inherits its level.
- OpenCode: restart its service (`opencode service restart`) only from such a terminal; a service started from a
  normal terminal runs the bridge at the lower level.
- After a Visual Studio update, check that its shortcut still has *Run as administrator* if you rely on it.

## Limitations

- **Files that are not UTF-8** (for example windows-1251 with non-ASCII characters): the agents' own edit tools read
  them as UTF-8 and can replace those characters irreversibly. The bridge cannot repair this.
- **Mixed line endings**: after an agent rewrites such a file, only the BOM and the final newline are restored; line
  endings stay as the agent wrote them.
- **Copies in `Output`** written by an agent's write tool keep the agent's format (LF, no BOM), so comparing a copy
  with the original shows every line as changed.
- **Deleting, moving and renaming** are not supported - only refused and listed (`_pending-operations.txt` in the
  session's folder under `Output`).
- OpenCode's permission prompt for `vsab_create_file` shows only the tool name, not the path and content.

## Uninstalling

There is no uninstaller: `init` cannot know whether you changed the same files later. Undo its changes by hand
(see [What init changes](#what-init-changes); the files as they were before the bridge are in
`<install-dir>\Settings\<agent>\init\`, see [Backups of your settings](#backups-of-your-settings)):

- Claude Code: `claude plugin uninstall vsagentbridge@vsab`, then `claude plugin marketplace remove vsab`; remove the
  `Output` entry from `permissions.additionalDirectories`. With the alternative way: remove `CLAUDE_CODE_PLUGIN_DIRS`
  from `env` and `vsagentbridge@inline` from `pluginConfigs`.
- OpenCode: remove the bridge entries from `opencode.json` in each workspace root and restart the service.
- Remove the bridge's rules block from your global `CLAUDE.md` / `AGENTS.md` - everything from the line
  `<!-- vsab:begin -->` to the line `<!-- vsab:end -->`, including these two lines - and the rules from per-folder
  files, then delete `<install-dir>`.

## License

VS Agent Bridge is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE)
(<https://polyformproject.org/licenses/noncommercial/1.0.0>). It is not open source.

- **Free for noncommercial use:** personal use (study, experiments, hobby projects) and use by noncommercial
  organizations (charities, educational and public research institutions, government institutions and others
  listed in the license).
- **Any other use requires a separate license from the author** - including using the bridge for your work in a
  company. To request one, write to [vsagentbridge@gmail.com](mailto:vsagentbridge@gmail.com).
- You may change your copy and share it for noncommercial purposes, under the same license and with the
  `Required Notice` line from `LICENSE`.

Third-party libraries shipped with the bridge and their licenses are listed in `THIRD-PARTY-NOTICES.txt`
(next to `vsagentbridge.exe` in the package; `VSAgentBridge/THIRD-PARTY-NOTICES.txt` in the repository).

## Contributions

External pull requests are not accepted.
If you want to report a problem or suggest an improvement, please open an Issue.
See [CONTRIBUTING.md](CONTRIBUTING.md) for details.
