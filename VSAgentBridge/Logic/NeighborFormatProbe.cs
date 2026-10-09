using System.Text;

namespace VSAgentBridge.Logic;

/// <summary>
/// Level 2 of the new-file format (slice 5 results, §1.2): line endings and BOM of the nearest files with the same
/// extension. Looked up in the file's folder, then upwards to the scope folder (the project's folder, or the
/// solution's for a file outside projects); if none are there, anywhere under the scope folder. Up to
/// <see cref="MaxFiles"/> files, the start of each is read. The majority decides; a tie gives the Visual Studio
/// default (CRLF, BOM). The final newline is not taken from neighbors. Unreadable files are skipped.
/// </summary>
internal static class NeighborFormatProbe
{
    public const int MaxFiles = 5;
    private const int SampleBytes = 64 * 1024;

    // Build output and tool folders: their files are not the project's style.
    private static readonly HashSet<string> SkippedFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".vs", ".git", "node_modules", "packages" };

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <param name="filePath">The new file; it may already exist (the hook's "after"): it is never counted.</param>
    /// <param name="scopeDirectory">The search does not go above this folder.</param>
    public static NewFileFormat Probe(string filePath, string scopeDirectory)
    {
        var fullPath = Path.GetFullPath(filePath);
        var extension = Path.GetExtension(fullPath);
        if (extension.Length == 0)
            return NewFileFormat.None;

        var scope = Path.GetFullPath(scopeDirectory);
        var neighbors = FindNeighbors(fullPath, extension, scope);

        int crlf = 0, lf = 0, bom = 0, noBom = 0;
        var notUtf8 = false;
        foreach (var neighbor in neighbors)
        {
            var sample = ReadSample(neighbor);
            if (sample == null)
                continue;

            var hasBom = sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF;
            if (hasBom) bom++;
            else
            {
                noBom++;
                if (!IsUtf8(sample)) notUtf8 = true;
            }

            var (crlfCount, lfCount) = CountLineEndings(sample);
            if (crlfCount > 0 || lfCount > 0)
            {
                if (crlfCount >= lfCount) crlf++;
                else lf++;
            }
        }

        bool? crlfValue = crlf + lf == 0 ? null : crlf >= lf;
        bool? bomValue = bom + noBom == 0 ? null : bom >= noBom;
        return new NewFileFormat(crlfValue, bomValue, finalNewline: null, NewFileFormat.SourceNeighbors,
            neighborNotUtf8: bomValue == false && notUtf8);
    }

    // Nearest first: the file's folder (or its nearest existing ancestor: the folder may be new) up to the scope,
    // then the whole scope.
    private static List<string> FindNeighbors(string fullPath, string extension, string scope)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fullPath };

        try
        {
            for (var dir = Path.GetDirectoryName(fullPath); dir != null && PathUtil.IsUnder(dir, scope);
                 dir = Path.GetDirectoryName(dir))
            {
                if (!Directory.Exists(dir))
                    continue;
                Collect(Directory.EnumerateFiles(dir, "*" + extension), extension, seen, found);
                if (found.Count >= MaxFiles)
                    return found;
            }

            if (found.Count == 0 && Directory.Exists(scope))
                Collect(EnumerateTree(scope, extension), extension, seen, found);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // What was found so far is enough to decide, or nothing is: then the default applies.
        }

        return found;
    }

    private static void Collect(IEnumerable<string> files, string extension, HashSet<string> seen, List<string> found)
    {
        foreach (var file in files)
        {
            // "*.cs" also returns "*.csproj" on some file systems (8.3 names): the extension is checked exactly.
            if (!string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase) || !seen.Add(file))
                continue;
            found.Add(file);
            if (found.Count >= MaxFiles)
                return;
        }
    }

    // Lazy, folder by folder, skipping build output: stops as soon as enough files are found.
    private static IEnumerable<string> EnumerateTree(string root, string extension)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var dir = pending.Dequeue();
            string[] files, subdirs;
            try
            {
                files = Directory.GetFiles(dir, "*" + extension);
                subdirs = Directory.GetDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;
            foreach (var sub in subdirs)
                if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                    pending.Enqueue(sub);
        }
    }

    private static byte[]? ReadSample(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[(int)Math.Min(SampleBytes, stream.Length)];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0) break;
                read += n;
            }

            // A cut sample may end in the middle of a UTF-8 character: it is cut at its last line break instead.
            if (read < stream.Length)
            {
                var lastLf = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
                if (lastLf >= 0) read = lastLf + 1;
            }

            if (read == buffer.Length) return buffer;
            var sample = new byte[read];
            Array.Copy(buffer, sample, read);
            return sample;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsUtf8(byte[] sample)
    {
        try
        {
            StrictUtf8.GetCharCount(sample);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static (int Crlf, int Lf) CountLineEndings(byte[] sample)
    {
        int crlf = 0, lf = 0;
        for (var i = 0; i < sample.Length; i++)
        {
            if (sample[i] != (byte)'\n') continue;
            if (i > 0 && sample[i - 1] == (byte)'\r') crlf++;
            else lf++;
        }
        return (crlf, lf);
    }
}