# Rules for TFVC workspaces only (VS Agent Bridge)

Before you follow any rule below, check your working directory
(the "Working directory" line of your environment information):
- If it is under <TFVC workspace root>, follow every rule below.
- Otherwise, ignore this whole file and work as usual: there you may create, edit,
  delete, move and rename files with any tool, as the user asks.

Under <TFVC workspace root>, files are under TFVC (Team Foundation Version Control) and belong
to Visual Studio projects. VS Agent Bridge checks files out and adds new files to their projects,
but only when you use the file tools.

## Creating and changing files
- Change existing files with the edit tool. Do not rewrite a whole existing file with the write tool
  (it changes line endings and may change other things); if that is really simpler, say why first,
  and change nothing that was not asked for.
- Create new files with the write tool. The bridge adds them to their Visual Studio project
  (or to the solution) the way Visual Studio itself does: do not edit project or solution files
  (.csproj, .vbproj, .sqlproj, .sln, ...) to include new files.
- Never create, change, copy or empty files with shell commands or scripts (>, Set-Content, Out-File,
  Copy-Item, sed -i, ...), and never make a read-only file writable (attrib -R and the like):
  a read-only file is simply not checked out yet, and the edit tool takes care of that.

## Deleting, moving and renaming
- Do not delete, move or rename files or folders, with any tool, shell command or script.
  The user does it in Visual Studio (for SQL objects, with the SSDT Rename refactoring),
  so that the projects and TFVC stay consistent.
- Do not change references to such files yet.
- Before you report such a file, search the code for references to it and to what it defines
  (classes, procedures, tables).
- At the end of your answer, list each file to delete, move or rename (with its new name or place)
  and every place that references it.

## When VS Agent Bridge answers
- If the bridge blocks a call or saves a file elsewhere, follow its message exactly.
  Do not look for another way to do the same thing.

## Version control
- Do not run tf (TFVC) commands and do not suggest checking in: the user decides.