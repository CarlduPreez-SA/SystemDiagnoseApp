using System.Diagnostics.Eventing.Reader;
using System.Text;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// Repeated critical/error events in the System log (disk timeouts, NTFS corruption,
/// unexpected shutdowns, service crashes) point straight at hardware or driver trouble.
/// </summary>
public sealed class EventLogErrorsCheck : IDiagnosticCheck
{
    public string Id => "event-log-errors";
    public string Title => "Recent Windows errors (last 7 days)";
    public int Order => 100;

    private static readonly HashSet<string> HighSignalSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "disk", "Disk", "Ntfs", "volmgr", "storahci", "stornvme", "nvme",
        "Microsoft-Windows-Kernel-Power", "Microsoft-Windows-DiskDiagnostic",
        "Microsoft-Windows-Ntfs", "BugCheck", "Microsoft-Windows-WHEA-Logger",
    };

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(() => Run(cancellationToken), cancellationToken);

    private DiagnosticResult Run(CancellationToken ct)
    {
        var since = DateTime.Now.AddDays(-7);
        long ms = (long)(DateTime.Now - since).TotalMilliseconds;

        // Level 1 = Critical, 2 = Error
        string q = $"*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) &lt;= {ms}]]]";
        var counts = new Dictionary<string, (int Count, string Sample, bool HighSignal)>(StringComparer.OrdinalIgnoreCase);
        int total = 0;

        try
        {
            foreach (var logName in new[] { "System", "Application" })
            {
                ct.ThrowIfCancellationRequested();
                var query = new EventLogQuery(logName, PathType.LogName, q) { ReverseDirection = true };
                using var reader = new EventLogReader(query);

                for (EventRecord? rec = reader.ReadEvent(); rec is not null; rec = reader.ReadEvent())
                {
                    using (rec)
                    {
                        total++;
                        string source = rec.ProviderName ?? "(unknown)";
                        string key = $"{source} / event {rec.Id}";
                        bool high = HighSignalSources.Contains(source) || rec.LevelDisplayName == "Critical";

                        string sample = "";
                        try { sample = rec.FormatDescription() ?? ""; } catch { /* ignore */ }
                        sample = FirstLine(sample);

                        if (counts.TryGetValue(key, out var existing))
                            counts[key] = (existing.Count + 1, existing.Sample, existing.HighSignal || high);
                        else
                            counts[key] = (1, sample, high);
                    }

                    if (total > 2000) break;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return DiagnosticResult.Error(Id, Title, "Access to the Windows event log was denied. Run the app as administrator.");
        }
        catch (Exception ex)
        {
            return DiagnosticResult.Error(Id, Title, $"Could not read the event log: {ex.Message}");
        }

        if (total == 0)
            return DiagnosticResult.Create(Id, Title, Severity.Ok, "No critical or error events in the last 7 days.");

        var sb = new StringBuilder($"{total} error/critical event(s) in the last 7 days. Most frequent:").AppendLine();
        foreach (var (key, v) in counts.OrderByDescending(kv => kv.Value.HighSignal).ThenByDescending(kv => kv.Value.Count).Take(10))
        {
            sb.AppendLine($"  {v.Count,3}×  {key}{(v.HighSignal ? "   <-- hardware/disk related" : "")}");
            if (v.Sample.Length > 0) sb.AppendLine($"        e.g. {Truncate(v.Sample)}");
        }

        bool hardware = counts.Values.Any(v => v.HighSignal && v.Count >= 2);
        var severity = hardware ? Severity.Critical
            : counts.Values.Any(v => v.Count >= 10) ? Severity.Warning
            : Severity.Info;

        string recommendation = hardware
            ? "Repeated disk/hardware errors are logged. Combined with the disk health result, this strongly suggests a failing drive or cable. Back up important files and check the disk (chkdsk, and the drive maker's diagnostic tool)."
            : severity == Severity.Warning
                ? "One component is failing repeatedly. Look up the top source/event ID above to identify the culprit (often a specific app or service that should be updated or reinstalled)."
                : "Some errors are normal. Worth a glance, not urgent.";

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), recommendation);
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        int nl = s.IndexOfAny(['\r', '\n']);
        return (nl < 0 ? s : s[..nl]).Trim();
    }

    private static string Truncate(string s) => s.Length <= 140 ? s : s[..137] + "...";
}
