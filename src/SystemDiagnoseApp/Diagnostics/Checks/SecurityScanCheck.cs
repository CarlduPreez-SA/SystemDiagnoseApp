using System.Text;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// A running antivirus scan, or two antivirus products fighting each other, will
/// make a slow PC unusable. Also flags very old virus definitions.
/// </summary>
public sealed class SecurityScanCheck : IDiagnosticCheck
{
    public string Id => "security-scan";
    public string Title => "Antivirus status";
    public int Order => 80;

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        var sb = new StringBuilder();
        var severity = Severity.Ok;
        var recommendation = string.Empty;

        var products = CimService.TryQuery("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2");
        if (products.Count > 0)
        {
            sb.AppendLine("Registered antivirus products:");
            foreach (var p in products)
            {
                int state = p.GetInt("productState") ?? 0;
                bool enabled = (state & 0x1000) != 0;
                bool upToDate = (state & 0x10) == 0;
                sb.AppendLine($"  {p.GetString("displayName")}  (realtime: {(enabled ? "on" : "off")}, definitions: {(upToDate ? "current" : "out of date")})");
            }

            var thirdParty = products
                .Select(p => p.GetString("displayName") ?? "")
                .Where(n => !n.Contains("Defender", StringComparison.OrdinalIgnoreCase) && n.Length > 0)
                .ToList();
            if (thirdParty.Count > 1 ||
                (thirdParty.Count == 1 && products.Any(p => (p.GetString("displayName") ?? "").Contains("Defender", StringComparison.OrdinalIgnoreCase) && ((p.GetInt("productState") ?? 0) & 0x1000) != 0)))
            {
                severity = severity.Worse(Severity.Warning);
                recommendation = "More than one real-time antivirus appears active. Two AV engines scanning every file at once is a big slowdown. Keep one (Windows Defender is fine) and remove the others.";
            }
        }

        var def = CimService.TryQuery(
            "SELECT AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, QuickScanAge, FullScanAge, AntivirusSignatureAge FROM MSFT_MpComputerStatus",
            @"root\Microsoft\Windows\Defender").FirstOrDefault();

        if (def is not null)
        {
            long? sigAge = def.GetLong("AntivirusSignatureAge");
            sb.AppendLine();
            sb.AppendLine($"Windows Defender: realtime {(def.GetBool("RealTimeProtectionEnabled") == true ? "on" : "off")}, " +
                          $"signatures {(sigAge is null ? "unknown" : sigAge + " day(s) old")}.");
            if (sigAge is > 14)
            {
                severity = severity.Worse(Severity.Warning);
                if (recommendation.Length == 0)
                    recommendation = "Virus definitions are stale. Update them (Windows Security → Virus & threat protection → Check for updates) — an outdated engine keeps rescanning.";
            }
        }

        var scanning = ProcessIsBusy("MsMpEng") || ProcessIsBusy("MpCmdRun");
        if (scanning)
        {
            severity = severity.Worse(Severity.Info);
            sb.AppendLine();
            sb.AppendLine("Defender's scanning engine is using noticeable CPU right now — a scan may be in progress.");
            if (recommendation.Length == 0)
                recommendation = "An antivirus scan may be running. Let it finish, then re-test. If scans always make the PC unusable, schedule them for overnight.";
        }

        if (products.Count == 0 && def is null)
            return DiagnosticResult.Error(Id, Title, "Could not read antivirus status on this PC.");

        if (severity == Severity.Ok)
            sb.AppendLine().Append("Antivirus setup looks fine.");

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), recommendation);
    }

    private static bool ProcessIsBusy(string name)
    {
        try
        {
            var procs = System.Diagnostics.Process.GetProcessesByName(name);
            foreach (var p in procs)
            {
                using (p)
                {
                    try { if (p.TotalProcessorTime.TotalSeconds > 30) return true; }
                    catch { /* ignore */ }
                }
            }
        }
        catch { /* ignore */ }
        return false;
    }
}
