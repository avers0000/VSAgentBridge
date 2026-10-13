using System.Globalization;
using System.Text;

namespace VSAgentBridge.Logic;

/// <summary>
/// Copies of the files one init run changes, and the change itself, under &lt;install&gt;\Settings\&lt;harness&gt;\:
/// init\ holds the originals from the first run, &lt;yyyyMMdd-HHmmss&gt;\before\ and \after\ each run's states.
/// Inside these folders the original's full path is repeated ("C:\Users\x\a.json" as "C\Users\x\a.json"),
/// so files with the same name from different folders do not collide. I/O errors are left to the caller.
/// </summary>
internal sealed class SettingsBackup
{
    public const string FolderName = "Settings";
    public const string InitFolderName = "init";
    public const string BeforeFolderName = "before";
    public const string AfterFolderName = "after";

    private SettingsBackup(string runFolder, string initFolder, bool isFirstRun)
    {
        RunFolder = runFolder;
        InitFolder = initFolder;
        IsFirstRun = isFirstRun;
    }

    /// <summary>The folder of this run: &lt;install&gt;\Settings\&lt;harness&gt;\&lt;yyyyMMdd-HHmmss&gt;.</summary>
    public string RunFolder { get; }

    /// <summary>&lt;install&gt;\Settings\&lt;harness&gt;\init: the originals from before the bridge changed anything.</summary>
    public string InitFolder { get; }

    /// <summary>True if init\ did not exist before this run: originals are copied there as well.</summary>
    public bool IsFirstRun { get; }

    /// <summary>
    /// Creates the folder of this run (and init\ on the first run). Call it only once the user has confirmed
    /// the changes, so that nothing is created when they are declined.
    /// </summary>
    public static SettingsBackup Start(string installDirectory, string harness, DateTime now)
    {
        var harnessFolder = Path.Combine(installDirectory, FolderName, harness);
        var initFolder = Path.Combine(harnessFolder, InitFolderName);
        var isFirstRun = !Directory.Exists(initFolder);

        var name = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var runFolder = Path.Combine(harnessFolder, name);
        for (var i = 2; Directory.Exists(runFolder); i++)
            runFolder = Path.Combine(harnessFolder, $"{name}-{i}");

        Directory.CreateDirectory(runFolder);
        if (isFirstRun)
            Directory.CreateDirectory(initFolder);
        return new SettingsBackup(runFolder, initFolder, isFirstRun);
    }

    /// <summary>The text of <paramref name="path"/> (a BOM removed), or null if the file does not exist.</summary>
    public static string? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>
    /// The original's full path as relative folders: "C:\a\b.json" becomes "C\a\b.json",
    /// "\\server\share\b.json" becomes "UNC\server\share\b.json".
    /// </summary>
    public static string MirrorPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return "UNC" + full.Substring(1);
        return full.Substring(0, 1) + full.Substring(2);
    }

    /// <summary>The copy of <paramref name="path"/> in this run's before\ folder.</summary>
    public string BeforePath(string path) => Path.Combine(RunFolder, BeforeFolderName, MirrorPath(path));

    /// <summary>The copy of <paramref name="path"/> in this run's after\ folder.</summary>
    public string AfterPath(string path) => Path.Combine(RunFolder, AfterFolderName, MirrorPath(path));

    /// <summary>
    /// Copies <paramref name="path"/> as it is now to before\ (and to init\ on the first run).
    /// Does nothing if the file does not exist. Call it before anything changes the file.
    /// </summary>
    public void SaveBefore(string path)
    {
        if (!File.Exists(path))
            return;

        CopyFile(path, BeforePath(path));
        if (IsFirstRun)
            CopyFile(path, Path.Combine(InitFolder, MirrorPath(path)));
    }

    /// <summary>
    /// Writes <paramref name="content"/> to after\ and then copies it over <paramref name="path"/>
    /// (creating its folder if needed). UTF-8, with a BOM only if the original had one.
    /// </summary>
    public void Write(string path, string content)
    {
        var after = AfterPath(path);
        var bom = File.Exists(path) && StartsWithBom(path);
        Directory.CreateDirectory(Path.GetDirectoryName(after)!);
        File.WriteAllText(after, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.Copy(after, path, overwrite: true);
    }

    /// <summary>
    /// Copies <paramref name="path"/> as it is now to after\: for a file changed by an agent's command
    /// rather than by <see cref="Write"/>. Does nothing if the file does not exist.
    /// </summary>
    public void SaveAfter(string path)
    {
        if (File.Exists(path))
            CopyFile(path, AfterPath(path));
    }

    private static void CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    private static bool StartsWithBom(string path)
    {
        var head = new byte[3];
        using var stream = File.OpenRead(path);
        return stream.Read(head, 0, 3) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
    }
}