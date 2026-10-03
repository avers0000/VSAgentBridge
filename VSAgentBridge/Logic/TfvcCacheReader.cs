using System.Xml;

namespace VSAgentBridge.Logic;

/// <summary>
/// Finds and reads the TFVC client's workspace cache files under %LOCALAPPDATA%\Microsoft.
/// Each Visual Studio version may keep its own copy, so all of them are returned.
/// </summary>
internal static class TfvcCacheReader
{
    private const string CacheFileName = "VersionControl.config";
    private const int MaxAttempts = 3;
    private const int RetryDelayMs = 50;

    // Known layouts:
    //   <product>\<ver>\Cache\Volatile\<collection-guid>_http\VersionControl.config
    //   <product>\<ver>\Cache\VersionControl.config   (older clients)
    private static readonly string[] ProductFolders = { "Azure DevOps", "Team Foundation" };

    public static IReadOnlyList<TfvcCacheFile> ReadAll() =>
        ReadAll(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static IReadOnlyList<TfvcCacheFile> ReadAll(string localAppData) =>
        FindFiles(localAppData).Select(Read).ToList();

    public static IReadOnlyList<string> FindFiles(string localAppData)
    {
        var result = new List<string>();
        var microsoft = Path.Combine(localAppData, "Microsoft");

        foreach (var product in ProductFolders)
        {
            foreach (var versionDir in GetDirectoriesSafe(Path.Combine(microsoft, product)))
            {
                var cacheDir = Path.Combine(versionDir, "Cache");
                AddIfExists(result, Path.Combine(cacheDir, CacheFileName));

                foreach (var collectionDir in GetDirectoriesSafe(Path.Combine(cacheDir, "Volatile")))
                    AddIfExists(result, Path.Combine(collectionDir, CacheFileName));
            }
        }

        return result;
    }

    public static TfvcCacheFile Read(string filePath)
    {
        var lastWrite = default(DateTime);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                lastWrite = File.GetLastWriteTimeUtc(filePath);

                string xml;
                // Visual Studio may have the file open for writing; let it, and read what is there.
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
                    xml = reader.ReadToEnd();

                return new TfvcCacheFile(filePath, lastWrite, TfvcWorkspaceCache.Parse(xml), null);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < MaxAttempts)
            {
                Thread.Sleep(RetryDelayMs);
            }
            catch (Exception ex) when (IsTransient(ex) || ex is UnauthorizedAccessException)
            {
                return new TfvcCacheFile(filePath, lastWrite, Array.Empty<TfvcWorkspace>(),
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // A file caught in the middle of being rewritten shows up as a sharing violation or as truncated XML.
    private static bool IsTransient(Exception ex) => ex is IOException || ex is XmlException;

    private static void AddIfExists(List<string> list, string path)
    {
        if (File.Exists(path))
            list.Add(path);
    }

    private static string[] GetDirectoriesSafe(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}