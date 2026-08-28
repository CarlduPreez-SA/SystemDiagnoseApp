using System.Text;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// A device with a bad or missing driver — especially storage or chipset — makes
/// Windows fall back to slow generic modes (e.g. a disk stuck in PIO instead of DMA).
/// </summary>
public sealed class DriverProblemsCheck : IDiagnosticCheck
{
    public string Id => "driver-problems";
    public string Title => "Device & driver problems";
    public int Order => 90;

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        var bad = CimService.TryQuery(
                "SELECT Name, DeviceID, ConfigManagerErrorCode, PNPClass FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
            .Where(r => (r.GetInt("ConfigManagerErrorCode") ?? 0) != 0)
            .ToList();

        if (bad.Count == 0)
            return DiagnosticResult.Create(Id, Title, Severity.Ok, "No devices are reporting driver errors.");

        var sb = new StringBuilder($"{bad.Count} device(s) with problems:").AppendLine();
        var severity = Severity.Warning;

        foreach (var d in bad)
        {
            int code = d.GetInt("ConfigManagerErrorCode") ?? 0;
            string cls = d.GetString("PNPClass") ?? "";
            sb.AppendLine($"  • {d.GetString("Name")}  (class: {(cls.Length > 0 ? cls : "unknown")}, error {code}: {ErrorText(code)})");

            if (cls is "DiskDrive" or "SCSIAdapter" or "hdc" or "System")
            {
                severity = Severity.Critical;
            }
        }

        string rec = severity == Severity.Critical
            ? "A storage or system device has a driver problem, which can cripple disk speed. Open Device Manager, find the flagged device, and update or reinstall its driver (or install the maker's chipset/storage driver package)."
            : "Open Device Manager and resolve the devices with a yellow warning — update the driver, or uninstall then re-scan for hardware.";

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), rec);
    }

    private static string ErrorText(int code) => code switch
    {
        1 => "not configured correctly",
        3 => "driver may be corrupted",
        10 => "cannot start",
        18 => "reinstall the drivers",
        19 => "registry / configuration problem",
        28 => "drivers not installed",
        31 => "cannot load required drivers",
        37 or 39 or 40 or 41 => "driver failed to load",
        43 => "device reported a problem",
        _ => "see Device Manager",
    };
}
