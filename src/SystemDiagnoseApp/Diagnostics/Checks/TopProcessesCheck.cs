using System.Diagnostics;
using System.Text;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>Shows what is actually eating CPU and RAM right now (AV, updaters, OneDrive, search...).</summary>
public sealed class TopProcessesCheck : IDiagnosticCheck
{
    public string Id => "top-processes";
    public string Title => "Top resource-using programs";
    public int Order => 50;

    public async Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
    {
        var first = Sample();
        await Task.Delay(700, cancellationToken).ConfigureAwait(false);
        var second = Sample();

        int cores = Environment.ProcessorCount;
        var cpuByName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double elapsedMs = 700;

        foreach (var proc in second)
        {
            string name = proc.Name;
            var start = first.FirstOrDefault(p => p.Name == name);
            double delta = proc.CpuMs - start.CpuMs;
            if (delta <= 0) continue;
            double pct = delta / (elapsedMs * cores) * 100;
            cpuByName[name] = cpuByName.GetValueOrDefault(name) + pct;
        }

        var memByName = second
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.WorkingSet), StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("Highest CPU use (over ~1 second):");
        foreach (var (name, pct) in cpuByName.OrderByDescending(kv => kv.Value).Take(6))
            sb.AppendLine($"  {name,-28} {pct,5:0.0}%");
        if (cpuByName.Count == 0) sb.AppendLine("  (nothing notable)");

        sb.AppendLine();
        sb.AppendLine("Highest memory use:");
        foreach (var (name, ws) in memByName.OrderByDescending(kv => kv.Value).Take(6))
            sb.AppendLine($"  {name,-28} {Format.Bytes(ws)}");

        var severity = Severity.Info;
        var recommendation = string.Empty;
        var hog = cpuByName.OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (hog.Value >= 60 && !IsExpected(hog.Key))
        {
            severity = Severity.Warning;
            recommendation = $"'{hog.Key}' is using a lot of CPU. If it isn't something they actively use, consider disabling it at startup or uninstalling it.";
        }
        else if (hog.Value >= 60)
        {
            recommendation = $"'{hog.Key}' is busy right now — this is often temporary (a scan or update). Re-run this check in a few minutes to see if it settles.";
        }

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), recommendation);
    }

    private static bool IsExpected(string name) =>
        name is "System" or "Idle" or "Registry" or "MsMpEng" or "TiWorker" or "wuauclt" or "Memory Compression";

    private static List<(string Name, int Pid, double CpuMs, long WorkingSet)> Sample()
    {
        var list = new List<(string, int, double, long)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                list.Add((p.ProcessName, p.Id, p.TotalProcessorTime.TotalMilliseconds, p.WorkingSet64));
            }
            catch
            {
                // Access denied for some protected processes — ignore.
            }
            finally { p.Dispose(); }
        }
        return list;
    }
}
