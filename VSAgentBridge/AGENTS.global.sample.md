# Rules for folders with VS Agent Bridge only

Before you follow any rule below, check your working directory
(the "Working directory" line of your environment information):
- If it is under one of these folders, follow every rule below:
  - <folder with VS Agent Bridge>
- Otherwise, ignore this whole file and work as usual: there you may create, edit,
  delete, move and rename files with any tool, as the user asks.

In these folders, files belong to Visual Studio projects, and some are under TFVC
(Team Foundation Version Control). VS Agent Bridge adds new files to their projects
and checks files out of TFVC, but only when you use the file tools.

## Creating and changing files
- Change existing files with the edit tool. Do not rewrite a whole existing file with the write tool
  (it changes line endings and may change other things); if that is really simpler, say why first,
  and change nothing that was not asked for.
- To create a new file, use the create_file tool of VS Agent Bridge first (its name contains
  "vsab" and ends with "create_file"; the prefix depends on the agent, for example
  vsab_create_file). It is a regular tool that you call directly: it is not in the Code Mode
  tool catalog, and you do not need search to find it. It writes the file and adds it to its
  Visual Studio project the way Visual Studio itself does, so you do not edit project or
  solution files (.csproj, .vbproj, .sqlproj, .sln, ...) to include new files.
- The write tool is the fallback for new files: use it when create_file answers that you
  should use the write tool, or when you have no create_file tool. The bridge then adds
  the new file to its project as well.
- Never create, change, copy or empty files with shell commands or scripts (>, Set-Content, Out-File,
  Copy-Item, sed -i, ...), and never make a read-only file writable (attrib -R and the like):
  under TFVC a read-only file is simply not checked out yet, and the edit tool takes care of that.

## Deleting, moving and renaming
- Do not delete, move or rename files or folders, with any tool, shell command or script.
  The user does it in Visual Studio (for SQL objects, with the SSDT Rename refactoring),
  so that the projects and version control stay consistent.
- Do not change references to such files yet.
- Before you report such a file, search the code for references to it and to what it defines
  (classes, procedures, tables).
- At the end of your answer, list each file to delete, move or rename (with its new name or place)
  and every place that references it.

## When VS Agent Bridge answers
- If the bridge blocks a call or saves a file elsewhere, follow its message exactly.
  Do not look for another way to do the same thing.

## Version control
- Version control (TFVC, git) is the user's business. Before you run a command that changes anything
  in it (checkout, add, undo, commit, reset, stash, push, ...), ask the user and wait for the answer.
  Commands that only read (status, diff, log, history) need no asking.