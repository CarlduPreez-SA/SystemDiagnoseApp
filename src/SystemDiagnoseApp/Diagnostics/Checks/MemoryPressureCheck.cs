using System.Text;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>Too little RAM forces Windows to page to disk constantly — everything crawls.</summary>
public sealed class MemoryPressureCheck : IDiagnosticCheck
{
    public string Id => "memory-pressure";
    public string Title => "Memory (RAM)";
    public int Order => 40;

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        var os = CimService.TryQuery(
            "SELECT TotalVisibleMemorySize, FreePhysicalMemory, TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem")
            .FirstOrDefault();
        var cs = CimService.TryQuery("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();

        if (os is null)
            return DiagnosticResult.Error(Id, Title, "Windows did not report memory information.");

        long installedBytes = cs?.GetLong("TotalPhysicalMemory") ?? 0;
        long totalKb = os.GetLong("TotalVisibleMemorySize") ?? 0;
        long freeKb = os.GetLong("FreePhysicalMemory") ?? 0;
        long usedKb = Math.Max(0, totalKb - freeKb);
        double usedFraction = totalKb > 0 ? (double)usedKb / totalKb : 0;

        double installedGb = installedBytes / 1024d / 1024 / 1024;

        var sb = new StringBuilder();
        sb.AppendLine($"Installed RAM: {installedGb:0.#} GB");
        sb.AppendLine($"In use right now: {Format.Bytes(usedKb * 1024)} of {Format.Bytes(totalKb * 1024)} ({Format.Percent(usedFraction)})");
        sb.AppendLine($"Free right now: {Format.Bytes(freeKb * 1024)}");

        long committedKb = (os.GetLong("TotalVirtualMemorySize") ?? 0) - (os.GetLong("FreeVirtualMemory") ?? 0);
        sb.AppendLine($"Committed (RAM + page file in use): {Format.Bytes(committedKb * 1024)}");

        var severity = Severity.Ok;
        var recommendation = string.Empty;

        if (installedGb > 0 && installedGb < 4.1)
        {
            severity = severity.Worse(Severity.Warning);
            recommendation = $"Only {installedGb:0.#} GB of RAM. Modern Windows 10 plus a browser needs 8 GB to feel responsive. Adding RAM is a cheap, high-impact upgrade.";
        }

        if (usedFraction >= 0.90)
        {
            severity = severity.Worse(Severity.Critical);
            recommendation = "RAM is nearly exhausted, so Windows is constantly swapping to disk. Close unused programs and browser tabs; if this is normal for them, add more RAM.";
        }
        else if (usedFraction >= 0.80)
        {
            severity = severity.Worse(Severity.Warning);
            if (recommendation.Length == 0)
                recommendation = "RAM usage is high. Check the 'Top resource-using programs' result for what is consuming it.";
        }

        if (severity == Severity.Ok)
            sb.AppendLine().Append("Memory looks healthy.");

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), recommendation);
    }
}
