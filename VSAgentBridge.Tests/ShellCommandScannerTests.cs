using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class ShellCommandScannerTests
{
    private const ShellOperation Delete = ShellOperation.Delete;
    private const ShellOperation Move = ShellOperation.Move;
    private const ShellOperation Write = ShellOperation.Write;
    private const ShellOperation Copy = ShellOperation.Copy;
    private const ShellOperation ClearReadOnly = ShellOperation.ClearReadOnly;

    // ---- Delete / move ----

    [TestMethod]
    [DataRow(@"Remove-Item .\Old.cs", "Remove-Item", @".\Old.cs")]
    [DataRow("rm -rf bin obj", "rm", "bin|obj")]
    [DataRow(@"del /f /q D:\x\a.cs", "del", @"D:\x\a.cs")]
    [DataRow("erase a.cs", "erase", "a.cs")]
    [DataRow(@"rd /s /q D:\x\Folder", "rd", @"D:\x\Folder")]
    [DataRow("rmdir Folder", "rmdir", "Folder")]
    [DataRow("unlink a.cs", "unlink", "a.cs")]
    [DataRow("ri a.cs", "ri", "a.cs")]
    [DataRow(@"Remove-Item -Path ""D:\a b\x.cs"" -Force", "Remove-Item", @"D:\a b\x.cs")]
    [DataRow("Remove-Item -LiteralPath:x.cs -Recurse", "Remove-Item", "x.cs")]
    [DataRow("Remove-Item a.cs, b.cs", "Remove-Item", "a.cs|b.cs")]
    [DataRow("Remove-Item *.tmp -Exclude keep.tmp", "Remove-Item", "*.tmp")]
    [DataRow("rm -- -odd-name", "rm", "-odd-name")]
    [DataRow("/bin/rm x", "/bin/rm", "x")]
    [DataRow(@"C:\Windows\System32\cmd.exe /c del x", "del", "x")]
    [DataRow("REMOVE-ITEM X", "REMOVE-ITEM", "X")]
    [DataRow("Remove-Item $f", "Remove-Item", "")]
    [DataRow("Remove-Item \"$dir\\x.cs\"", "Remove-Item", "")]
    public void Delete_Simple(string command, string expectedCommand, string expectedPaths)
    {
        AssertSingle(command, Delete, expectedCommand, expectedPaths);
    }

    [TestMethod]
    [DataRow("Rename-Item X.cs Y.cs", "Rename-Item", "X.cs|Y.cs")]
    [DataRow("Rename-Item -Path X.cs -NewName Y.cs", "Rename-Item", "X.cs|Y.cs")]
    [DataRow("ren a.cs b.cs", "ren", "a.cs|b.cs")]
    [DataRow("rename a.cs b.cs", "rename", "a.cs|b.cs")]
    [DataRow("mv a.cs b.cs", "mv", "a.cs|b.cs")]
    [DataRow("mv -t Dir a.cs", "mv", "Dir|a.cs")]
    [DataRow("move a.cs Sub", "move", "a.cs|Sub")]
    [DataRow("Move-Item -Path a -Destination b", "Move-Item", "a|b")]
    [DataRow("mi a b", "mi", "a|b")]
    [DataRow("rni a b", "rni", "a|b")]
    public void Move_Simple(string command, string expectedCommand, string expectedPaths)
    {
        AssertSingle(command, Move, expectedCommand, expectedPaths);
    }

    [TestMethod]
    [DataRow("git rm a.cs", Delete, "git rm", "a.cs")]
    [DataRow("git mv a.cs b.cs", Move, "git mv", "a.cs|b.cs")]
    [DataRow("git -C D:\\repo rm --cached a.cs", Delete, "git rm", "a.cs")]
    [DataRow("git clean -fdx", Delete, "git clean", "")]
    [DataRow("tf delete $/Proj/a.cs", Delete, "tf delete", "$/Proj/a.cs")]
    [DataRow("tf destroy $/Proj/a.cs", Delete, "tf destroy", "$/Proj/a.cs")]
    [DataRow("tf rename a.cs b.cs /noprompt", Move, "tf rename", "a.cs|b.cs")]
    [DataRow("tf.exe move a b", Move, "tf move", "a|b")]
    [DataRow("robocopy src dst /MIR", Delete, "robocopy", "src|dst")]
    [DataRow("robocopy src dst /purge", Delete, "robocopy", "src|dst")]
    [DataRow("robocopy src dst *.cs /mov", Move, "robocopy", "src|dst")]
    [DataRow("robocopy src dst", Copy, "robocopy", "src|dst")]
    [DataRow("find . -name '*.bak' -delete", Delete, "find -delete", ".")]
    public void Tools(string command, ShellOperation operation, string expectedCommand, string expectedPaths)
    {
        AssertSingle(command, operation, expectedCommand, expectedPaths);
    }

    [TestMethod]
    [DataRow("Get-ChildItem *.tmp | Remove-Item")]
    [DataRow("ls | % { rm $_ }")]
    [DataRow("Get-ChildItem | ForEach-Object { Remove-Item $_.FullName }")]
    [DataRow("Get-ChildItem | ?{ $_.Length -eq 0 } | Remove-Item")]
    [DataRow("find . -name x -exec rm {} \\;")]
    [DataRow("find . -name x -exec rm -f {} +")]
    [DataRow("ls | xargs rm")]
    [DataRow("ls | xargs -n 1 -I {} rm {}")]
    [DataRow("sudo rm x")]
    [DataRow("start /b del x")]
    [DataRow("start \"\" del x")]
    [DataRow("call del x")]
    [DataRow("FOO=1 rm x")]
    [DataRow("echo $(rm x)")]
    [DataRow("dotnet build && del x.txt")]
    [DataRow("cd Sub; Remove-Item x")]
    [DataRow("cd Sub\nrm y")]
    [DataRow("if (Test-Path x) { Remove-Item x }")]
    [DataRow("& { Remove-Item x }")]
    [DataRow("wsl rm x")]
    [DataRow("wsl -d Ubuntu -e rm x")]
    [DataRow("Remove-Item `\r\n  x")]
    public void Delete_InsidePipelinesAndBlocks(string command)
    {
        AssertHas(command, Delete);
    }

    [TestMethod]
    [DataRow("powershell -NoProfile -Command \"Remove-Item x\"")]
    [DataRow("powershell -NoProfile -ExecutionPolicy Bypass -Command Remove-Item \"a b.cs\"")]
    [DataRow("powershell.exe -c 'Remove-Item x'")]
    [DataRow("pwsh -NoLogo -Com \"rm x\"")]
    [DataRow("powershell \"Remove-Item x\"")]
    [DataRow("cmd /c del x")]
    [DataRow("cmd /s /c \"del x\"")]
    [DataRow("cmd.exe /K del a")]
    [DataRow("bash -c \"rm -f x\"")]
    [DataRow("bash -lc 'rm a b'")]
    [DataRow("sh -c \"cd d && rm x\"")]
    [DataRow("iex \"Remove-Item x\"")]
    [DataRow("Invoke-Expression -Command 'del x'")]
    [DataRow("pwsh -c \"cmd /c 'del x'\"")]
    public void Delete_InNestedShells(string command)
    {
        AssertHas(command, Delete);
    }

    [TestMethod]
    [DataRow("[IO.File]::Delete(\"x.cs\")", Delete, "[IO.File]::Delete")]
    [DataRow("[System.IO.File]::Move('a', 'b')", Move, "[IO.File]::Move")]
    [DataRow("[io.file]::Replace('a', 'b', 'c')", Move, "[IO.file]::Replace")]
    [DataRow("[System.IO.Directory]::Delete('d', $true)", Delete, "[IO.Directory]::Delete")]
    [DataRow("[IO.Directory]::Move('a','b')", Move, "[IO.Directory]::Move")]
    [DataRow("[IO.File]::WriteAllText(\"x\", \"y\")", Write, "[IO.File]::WriteAllText")]
    [DataRow("[System.IO.File]::AppendAllLines('x', $l)", Write, "[IO.File]::AppendAllLines")]
    [DataRow("[IO.File]::Copy('a', 'b', $true)", Copy, "[IO.File]::Copy")]
    [DataRow("[IO.File]::SetAttributes('x', 'Normal')", ClearReadOnly, "[IO.File]::SetAttributes")]
    [DataRow("(Get-Item x.cs).IsReadOnly = $false", ClearReadOnly, "IsReadOnly = $false")]
    public void DotNet(string command, ShellOperation operation, string expectedCommand)
    {
        AssertSingle(command, operation, expectedCommand, "");
    }

    // ---- Write ----

    [TestMethod]
    [DataRow("echo hi > x.cs", ">", "x.cs")]
    [DataRow("echo hi >> \"D:\\a b\\x.cs\"", ">>", @"D:\a b\x.cs")]
    [DataRow("echo hi>x.cs", ">", "x.cs")]
    [DataRow("dir 1> list.txt", "1>", "list.txt")]
    [DataRow("build 2> err.txt", "2>", "err.txt")]
    [DataRow("x *> all.txt", "*>", "all.txt")]
    [DataRow("x &> all.txt", "&>", "all.txt")]
    [DataRow("echo hi >| x", ">|", "x")]
    [DataRow("dir > $env:TEMP\\x.txt", ">", "")]
    [DataRow("dir > %TEMP%\\x.txt", ">", "")]
    [DataRow("cat > x.cs <<'EOF'\nrm y\nEOF", ">", "x.cs")]
    public void Write_Redirections(string command, string expectedCommand, string expectedPaths)
    {
        AssertSingle(command, Write, expectedCommand, expectedPaths);
    }

    [TestMethod]
    [DataRow("Set-Content X.cs \"text\"", "Set-Content", "X.cs")]
    [DataRow("Set-Content -Path X.cs -Value \"a\" -Encoding UTF8", "Set-Content", "X.cs")]
    [DataRow("Set-Content -Value a X.cs", "Set-Content", "X.cs")]
    [DataRow("Add-Content x.cs 'line'", "Add-Content", "x.cs")]
    [DataRow("ac x.cs 'line'", "ac", "x.cs")]
    [DataRow("'a' | Out-File -FilePath x.txt -Append", "Out-File", "x.txt")]
    [DataRow("'a' | Out-File x.txt", "Out-File", "x.txt")]
    [DataRow("Clear-Content a.cs", "Clear-Content", "a.cs")]
    [DataRow("New-Item x.cs -ItemType File -Value 'a'", "New-Item", "x.cs")]
    [DataRow("New-Item -Path D:\\x -Name a.cs", "New-Item", "D:\\x|a.cs")]
    [DataRow("ni x.cs", "ni", "x.cs")]
    [DataRow("touch a b", "touch", "a|b")]
    [DataRow("echo x | tee out.txt", "tee", "out.txt")]
    [DataRow("echo x | Tee-Object -FilePath out.txt", "Tee-Object", "out.txt")]
    [DataRow("sed -i 's/a/b/' x.cs", "sed -i", "x.cs")]
    [DataRow("sed -i.bak -e 's/a/b/' x.cs y.cs", "sed -i", "x.cs|y.cs")]
    [DataRow("sed --in-place 's/a/b/' x.cs", "sed -i", "x.cs")]
    public void Write_Commands(string command, string expectedCommand, string expectedPaths)
    {
        AssertSingle(command, Write, expectedCommand, expectedPaths);
    }

    [TestMethod]
    [DataRow("Copy-Item a.cs b.cs", "Copy-Item", "a.cs|b.cs")]
    [DataRow("Copy-Item -Path a.cs -Destination D:\\x -Force", "Copy-Item", "a.cs|D:\\x")]
    [DataRow("cp -f a b", "cp", "a|b")]
    [DataRow("copy /y a.cs b.cs", "copy", "a.cs|b.cs")]
    [DataRow("xcopy /s src dst", "xcopy", "src|dst")]
    public void Copy_Commands(string command, string expectedCommand, string expectedPaths)
    {
        AssertSingle(command, Copy, expectedCommand, expectedPaths);
    }

    [TestMethod]
    [DataRow("attrib -R x.cs", "attrib", "x.cs")]
    [DataRow("attrib -r +a x.cs", "attrib", "x.cs")]
    [DataRow("chmod +w x.cs", "chmod", "x.cs")]
    [DataRow("chmod 644 x.cs", "chmod", "x.cs")]
    [DataRow("Set-ItemProperty x.cs -Name IsReadOnly -Value $false", "Set-ItemProperty", "x.cs")]
    public void ClearReadOnly_Commands(string command, string expectedCommand, string expectedPaths)
    {
        AssertSingle(command, ClearReadOnly, expectedCommand, expectedPaths);
    }

    [TestMethod]
    [DataRow(ShellOperation.Delete, true)]
    [DataRow(ShellOperation.Move, true)]
    [DataRow(ShellOperation.Write, false)]
    [DataRow(ShellOperation.Copy, false)]
    [DataRow(ShellOperation.ClearReadOnly, false)]
    public void IsDeleteOrMove(ShellOperation operation, bool expected)
    {
        Assert.AreEqual(expected, new ShellFinding(operation, "x", Array.Empty<string>()).IsDeleteOrMove);
    }

    [TestMethod]
    public void Write_HereStringContentIsData_NotCommands()
    {
        var findings = ShellCommandScanner.Scan("Set-Content x.cs @\"\r\nRemove-Item y\r\nmv a b\r\n\"@");

        Assert.AreEqual(1, findings.Count, Describe(findings));
        Assert.AreEqual(Write, findings[0].Operation);
        Assert.AreEqual("Set-Content", findings[0].Command);
    }

    [TestMethod]
    public void Write_InNestedShell()
    {
        AssertHas("powershell -Command \"Set-Content x.cs 'a'\"", Write);
        AssertHas("cmd /c \"echo a > x.cs\"", Write);
        AssertHas("bash -c 'echo a >> x.cs'", Write);
    }

    [TestMethod]
    public void Several_AllFoundInOrder()
    {
        var findings = ShellCommandScanner.Scan("Copy-Item a.cs b.cs; Remove-Item a.cs; echo done > log.txt");

        Assert.AreEqual("Copy Copy-Item|Delete Remove-Item|Write >", Describe(findings));
    }

    // ---- Nothing ----

    [TestMethod]
    [DataRow("dotnet build 2>&1")]
    [DataRow("git status 2>$null")]
    [DataRow("dir > NUL")]
    [DataRow("dir >nul 2>&1")]
    [DataRow("ls > /dev/null 2>&1")]
    [DataRow("x *> $null")]
    [DataRow("x | Out-Null")]
    [DataRow("x >&2")]
    [DataRow("sort < in.txt")]
    [DataRow("git status")]
    [DataRow("git commit -m \"rm old stuff\"")]
    [DataRow("git log --grep=mv")]
    [DataRow("grep -rn \"Remove-Item\" .")]
    [DataRow("echo \"del x\"")]
    [DataRow("echo rm")]
    [DataRow("Select-String -Pattern \"mv\" -Path *.cs")]
    [DataRow("Get-Content x.cs")]
    [DataRow("[IO.File]::ReadAllText(\"x\")")]
    [DataRow("[IO.File]::Exists(\"x\")")]
    [DataRow("tf status /recursive")]
    [DataRow("tf checkout x.cs")]
    [DataRow("mkdir x")]
    [DataRow("md x")]
    [DataRow("New-Item -ItemType Directory x")]
    [DataRow("ni x -ItemType dir")]
    [DataRow("New-Item -Type:Directory -Path x")]
    [DataRow("sed 's/a/b/' x.cs")]
    [DataRow("sed -n '1,5p' x.cs")]
    [DataRow("attrib x.cs")]
    [DataRow("attrib +r x.cs")]
    [DataRow("chmod +x run.sh")]
    [DataRow("Set-ItemProperty x -Name Attributes -Value Normal")]
    [DataRow("bash script.sh")]
    [DataRow("pwsh -File clean.ps1")]
    [DataRow("powershell -EncodedCommand AAAA")]
    [DataRow("echo a # rm x")]
    [DataRow("dotnet clean")]
    [DataRow("msbuild /t:Clean")]
    [DataRow("echo file.{cs,bak}")]
    [DataRow("Get-ChildItem -Recurse -Filter *.cs | Select-String rm")]
    [DataRow("cat <<EOF\nrm x\nEOF")]
    [DataRow("powershell -Command \"Get-ChildItem\"")]
    [DataRow("")]
    [DataRow("   ")]
    public void Nothing(string command)
    {
        var findings = ShellCommandScanner.Scan(command);
        Assert.AreEqual(0, findings.Count, $"{command} → {Describe(findings)}");
    }

    [TestMethod]
    [DataRow("\"'\"'\"")]
    [DataRow("$(( rm")]
    [DataRow("@\"")]
    [DataRow("rm <<")]
    [DataRow("> ")]
    [DataRow("}}} rm {{{")]
    [DataRow("powershell -Command")]
    [DataRow("cmd /c")]
    [DataRow("bash -c")]
    [DataRow("find . -exec")]
    [DataRow("git -C")]
    [DataRow("Remove-Item -Path")]
    [DataRow("New-Item -ItemType")]
    [DataRow("iex")]
    [DataRow("pwsh -c pwsh -c pwsh -c pwsh -c pwsh -c pwsh -c rm x")]
    public void AnyText_DoesNotThrow(string command)
    {
        ShellCommandScanner.Scan(command);
    }

    [TestMethod]
    [DataRow("rm", "rm")]
    [DataRow("/usr/bin/rm", "rm")]
    [DataRow(@"C:\Windows\System32\CMD.EXE", "cmd")]
    [DataRow("tf.exe", "tf")]
    [DataRow("Remove-Item", "remove-item")]
    [DataRow(".exe", ".exe")]
    public void CommandName_Normalized(string text, string expected)
    {
        Assert.AreEqual(expected, ShellCommandScanner.CommandName(text));
    }

    // ---- Helpers ----

    private static void AssertSingle(string command, ShellOperation operation, string expectedCommand, string expectedPaths)
    {
        var findings = ShellCommandScanner.Scan(command);
        Assert.AreEqual(1, findings.Count, $"{command} → {Describe(findings)}");
        Assert.AreEqual(operation, findings[0].Operation, command);
        Assert.AreEqual(expectedCommand, findings[0].Command, command);
        Assert.AreEqual(expectedPaths, string.Join("|", findings[0].Paths), command);
    }

    private static void AssertHas(string command, ShellOperation operation)
    {
        var findings = ShellCommandScanner.Scan(command);
        Assert.IsTrue(findings.Any(f => f.Operation == operation), $"{command} → {Describe(findings)}");
    }

    private static string Describe(IEnumerable<ShellFinding> findings) =>
        string.Join("|", findings.Select(f => $"{f.Operation} {f.Command}"));
}
