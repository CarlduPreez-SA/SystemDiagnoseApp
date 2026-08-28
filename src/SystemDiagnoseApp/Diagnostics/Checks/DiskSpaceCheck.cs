using System.IO;
using System.Text;
using SystemDiagnoseApp.Fixes;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>A nearly-full system drive makes Windows page heavily and stall.</summary>
public sealed class DiskSpaceCheck : IDiagnosticCheck
{
    public string Id => "disk-space";
    public string Title => "Free disk space";
    public int Order => 20;

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var sb = new StringBuilder();
        var severity = Severity.Ok;
        var recommendation = string.Empty;

        foreach (var drive in DriveInfo.GetDrives().Where(d => d is { IsReady: true, DriveType: DriveType.Fixed }))
        {
            double freeFraction = (double)drive.TotalFreeSpace / drive.TotalSize;
            bool isSystem = string.Equals(drive.Name, systemRoot, StringComparison.OrdinalIgnoreCase);

            sb.AppendLine(
                $"{(isSystem ? "→ " : "  ")}{drive.Name}  " +
                $"{Format.Bytes(drive.TotalFreeSpace)} free of {Format.Bytes(drive.TotalSize)} " +
                $"({Format.Percent(freeFraction)} free){(isSystem ? "  [Windows drive]" : "")}");

            if (!isSystem) continue;

            if (freeFraction < 0.05 || drive.TotalFreeSpace < 5L * 1024 * 1024 * 1024)
            {
                severity = severity.Worse(Severity.Critical);
                recommendation = "The Windows drive is almost full. Free up space now: empty the Recycle Bin, run Disk Cleanup, remove large unused programs, and move photos/videos off this PC.";
            }
            else if (freeFraction < 0.12 || drive.TotalFreeSpace < 15L * 1024 * 1024 * 1024)
            {
                severity = severity.Worse(Severity.Warning);
                recommendation = "The Windows drive is getting full. Aim for at least 15% free so Windows has room to work.";
            }
        }

        if (severity == Severity.Ok)
            sb.AppendLine().Append("Plenty of free space on all drives.");

        IReadOnlyList<IFix> fixes = severity >= Severity.Warning
            ? [TempCleanupFix.Create()]
            : [];

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), recommendation, fixes);
    }
}
