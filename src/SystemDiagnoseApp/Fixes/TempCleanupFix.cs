using System.IO;

namespace SystemDiagnoseApp.Fixes;

/// <summary>Deletes obviously-safe temporary files. Never touches user documents.</summary>
public static class TempCleanupFix
{
    public static IFix Create() => new DelegateFix(
        title: "Clear temporary files",
        confirmationText:
            "This deletes the contents of:\n" +
            "  • your user Temp folder (%TEMP%)\n" +
            "  • C:\\Windows\\Temp\n" +
            "These folders only hold throwaway files. It does NOT touch documents, photos, " +
            "downloads, the Recycle Bin, or installed programs. Files currently in use are skipped.",
        isReversible: false,
        apply: static (progress, ct) => Task.Run(() =>
        {
            long freed = 0;
            int files = 0;

            string[] targets =
            [
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
            ];

            foreach (var dir in targets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(dir)) continue;
                progress($"Cleaning {dir} ...");
                (long f, int c) = CleanDirectory(dir, ct, progress);
                freed += f;
                files += c;
            }

            string human = Services.Format.Bytes(freed);
            progress($"Done. Removed {files} items, about {human}.");
            return FixOutcome.Ok($"Cleared temporary files: {files} items, ~{human} recovered.");
        }, ct));

    private static (long bytes, int count) CleanDirectory(string dir, CancellationToken ct, FixProgress progress)
    {
        long bytes = 0;
        int count = 0;

        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir); }
        catch { return (0, 0); }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(entry))
                {
                    var size = DirectorySize(entry);
                    Directory.Delete(entry, recursive: true);
                    bytes += size;
                    count++;
                }
                else
                {
                    var info = new FileInfo(entry);
                    long size = info.Exists ? info.Length : 0;
                    info.Delete();
                    bytes += size;
                    count++;
                }
            }
            catch
            {
                // Locked or protected — skip it silently, this is best-effort.
            }
        }

        return (bytes, count);
    }

    private static long DirectorySize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }
}
