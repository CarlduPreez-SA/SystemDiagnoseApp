using System.Text;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>Plain hardware / OS summary so the report stands on its own.</summary>
public sealed class SystemSummaryCheck : IDiagnosticCheck
{
    public string Id => "system-summary";
    public string Title => "System summary";
    public int Order => 5;

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        var os = CimService.TryQuery(
            "SELECT Caption, Version, BuildNumber, OSArchitecture, LastBootUpTime, InstallDate FROM Win32_OperatingSystem").FirstOrDefault();
        var cs = CimService.TryQuery(
            "SELECT Manufacturer, Model, TotalPhysicalMemory, NumberOfProcessors FROM Win32_ComputerSystem").FirstOrDefault();
        var cpu = CimService.TryQuery(
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").FirstOrDefault();

        var sb = new StringBuilder();
        sb.AppendLine($"PC name: {Environment.MachineName}");
        if (cs is not null)
            sb.AppendLine($"Model: {cs.GetString("Manufacturer")?.Trim()} {cs.GetString("Model")?.Trim()}");
        if (os is not null)
        {
            sb.AppendLine($"Windows: {os.GetString("Caption")?.Trim()}  (build {os.GetString("BuildNumber")}, {os.GetString("OSArchitecture")})");
            var boot = CimService.ToDateTime(os.GetString("LastBootUpTime"));
            if (boot is not null)
                sb.AppendLine($"Last booted: {boot:yyyy-MM-dd HH:mm}  (up {(DateTime.Now - boot.Value).TotalHours:0.#} hours)");
            var install = CimService.ToDateTime(os.GetString("InstallDate"));
            if (install is not null)
                sb.AppendLine($"Windows installed: {install:yyyy-MM-dd}");
        }
        if (cpu is not null)
            sb.AppendLine($"CPU: {cpu.GetString("Name")?.Trim()}  ({cpu.GetInt("NumberOfCores")} cores / {cpu.GetInt("NumberOfLogicalProcessors")} threads, {cpu.GetInt("MaxClockSpeed")} MHz)");
        if (cs?.GetLong("TotalPhysicalMemory") is { } ram)
            sb.AppendLine($"RAM: {Format.Bytes(ram)}");

        int build = int.TryParse(os?.GetString("BuildNumber"), out var b) ? b : 0;
        var severity = Severity.Info;
        string rec = string.Empty;

        // Windows 10 (build < 22000) end of support: 2025-10-14.
        if (build is > 0 and < 22000)
        {
            rec = "This is Windows 10, which stops receiving security updates on 14 October 2025. If the hardware allows, plan to move to Windows 11; otherwise keep it off untrusted networks/downloads.";
        }

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), rec);
    }
}
