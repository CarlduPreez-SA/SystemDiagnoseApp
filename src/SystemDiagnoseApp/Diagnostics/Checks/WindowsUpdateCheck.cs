using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// Windows Update running in the background (wuauserv / TrustedInstaller) hammers the
/// disk and CPU and can make everything feel stuck. A machine that hasn't updated in
/// months is also usually one that keeps trying, and failing, in the background.
/// </summary>
public sealed class WindowsUpdateCheck : IDiagnosticCheck
{
    public string Id => "windows-update";
    public string Title => "Windows Update activity";
    public int Order => 60;

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        var sb = new StringBuilder();
        var severity = Severity.Ok;
        var recommendation = string.Empty;

        DateTime? lastInstall = ReadLastSuccessTime();
        DateTime? newestHotfix = NewestHotfixDate();
        DateTime? mostRecent = new[] { lastInstall, newestHotfix }.Where(d => d is not null).Max();

        if (mostRecent is not null)
        {
            int days = (int)(DateTime.Now - mostRecent.Value).TotalDays;
            sb.AppendLine($"Last successful update: {mostRecent:yyyy-MM-dd} ({days} days ago)");
            if (days > 90)
            {
                severity = severity.Worse(Severity.Warning);
                recommendation = "Windows hasn't updated successfully in a long time. It is probably retrying in the background repeatedly. Open Settings → Update & Security → Windows Update and let it fully install and reboot.";
            }
        }
        else
        {
            sb.AppendLine("Last successful update: could not determine.");
        }

        var busy = new List<string>();
        foreach (var name in new[] { "wuauserv", "TrustedInstaller", "TiWorker", "MoUsoCoreWorker", "usoclient" })
        {
            foreach (var p in SafeGetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        var cpu = p.TotalProcessorTime;
                        busy.Add($"{p.ProcessName} (CPU time so far {cpu.TotalMinutes:0.#} min, memory {Format.Bytes(p.WorkingSet64)})");
                    }
                    catch { busy.Add(p.ProcessName); }
                }
            }
        }

        if (busy.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Update-related processes running now:");
            foreach (var b in busy) sb.AppendLine($"  {b}");
            severity = severity.Worse(Severity.Info);
            if (recommendation.Length == 0)
                recommendation = "Windows Update is working in the background. If the PC has been slow only recently, let it finish (it can take an hour or more) and reboot, then re-test.";
        }
        else
        {
            sb.AppendLine().AppendLine("No Windows Update processes are active right now.");
        }

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), recommendation);
    }

    private static DateTime? ReadLastSuccessTime()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install");
            string? raw = key?.GetValue("LastSuccessTime") as string;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                return dt.ToLocalTime();
        }
        catch { /* ignore */ }
        return null;
    }

    private static DateTime? NewestHotfixDate()
    {
        DateTime? newest = null;
        foreach (var row in CimService.TryQuery("SELECT InstalledOn FROM Win32_QuickFixEngineering"))
        {
            string? raw = row.GetString("InstalledOn");
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                if (newest is null || dt > newest) newest = dt;
        }
        return newest;
    }

    private static IEnumerable<Process> SafeGetProcessesByName(string name)
    {
        try { return Process.GetProcessesByName(name); }
        catch { return []; }
    }
}
