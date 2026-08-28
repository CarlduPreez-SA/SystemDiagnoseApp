using System.IO;
using System.Text;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// Is the Windows drive a spinning hard disk, and is any disk reporting SMART
/// failure? A slow or dying disk is the most common reason an app takes minutes to open.
/// </summary>
public sealed class DiskHealthCheck : IDiagnosticCheck
{
    public string Id => "disk-health";
    public string Title => "Disk type & health";
    public int Order => 10;

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        var sb = new StringBuilder();
        var severity = Severity.Ok;
        var recommendation = string.Empty;

        int? osDiskIndex = FindOsDiskIndex();
        var physicalDisks = CimService.TryQuery(
            "SELECT DeviceId, FriendlyName, MediaType, HealthStatus, Size, BusType FROM MSFT_PhysicalDisk",
            @"root\Microsoft\Windows\Storage");
        var win32Disks = CimService.TryQuery(
            "SELECT Index, Model, Status, Size, InterfaceType FROM Win32_DiskDrive");

        if (win32Disks.Count == 0)
            return DiagnosticResult.Error(Id, Title, "No disk drives were reported by Windows.");

        foreach (var disk in win32Disks.OrderBy(d => d.GetInt("Index") ?? 0))
        {
            int index = disk.GetInt("Index") ?? -1;
            string model = disk.GetString("Model")?.Trim() ?? "Unknown drive";
            string status = disk.GetString("Status") ?? "Unknown";
            bool isOsDisk = index == osDiskIndex;

            var phys = physicalDisks.FirstOrDefault(p => (p.GetString("DeviceId") ?? "") == index.ToString());
            string mediaType = MediaTypeName(phys?.GetInt("MediaType"));
            string health = HealthName(phys?.GetInt("HealthStatus"));

            sb.Append(isOsDisk ? "→ " : "  ");
            sb.Append($"Disk {index}: {model}");
            sb.Append($"  ({mediaType}, {Format.Bytes(disk.GetLong("Size"))}, Windows status: {status}");
            if (health is not "Unknown") sb.Append($", health: {health}");
            sb.AppendLine(")");

            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(status) && status != "Unknown")
            {
                severity = Max(severity, Severity.Critical);
                recommendation = "Windows reports a problem with a disk. Back up important files now and plan to replace the drive.";
            }

            if (health is "Warning" or "Unhealthy")
            {
                severity = Max(severity, Severity.Critical);
                recommendation = "A disk is reporting poor health. Back up important files now and replace the drive.";
            }

            if (isOsDisk && mediaType == "Hard disk (HDD)")
            {
                severity = Max(severity, Severity.Warning);
                if (recommendation.Length == 0)
                    recommendation = "Windows is running from a mechanical hard disk. Upgrading to an SSD is the single biggest speed improvement for this symptom.";
            }
        }

        // SMART failure prediction
        var smart = CimService.TryQuery(
            "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", @"root\wmi");
        var failing = smart.Where(s => s.GetBool("PredictFailure") == true).ToList();
        if (failing.Count > 0)
        {
            severity = Max(severity, Severity.Critical);
            sb.AppendLine();
            sb.AppendLine("SMART is predicting failure on:");
            foreach (var f in failing) sb.AppendLine($"  {f.GetString("InstanceName")}");
            recommendation = "A drive is predicting its own failure (SMART). Back up important files immediately and replace it.";
        }
        else if (smart.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("SMART failure prediction: no drives flagged.");
        }

        if (severity == Severity.Ok)
        {
            recommendation = string.Empty;
            sb.AppendLine();
            sb.AppendLine("No disk health problems found.");
        }

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), recommendation);
    }

    private static int? FindOsDiskIndex()
    {
        try
        {
            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

            var partitions = CimService.Query(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDrive}'}} WHERE ResultClass=Win32_DiskPartition");
            foreach (var partition in partitions)
            {
                string? partitionId = partition.GetString("DeviceID");
                if (partitionId is null) continue;

                var drives = CimService.Query(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} WHERE ResultClass=Win32_DiskDrive");
                var idx = drives.Select(d => d.GetInt("Index")).FirstOrDefault(i => i is not null);
                if (idx is not null) return idx;
            }
        }
        catch
        {
            // fall through
        }
        return null;
    }

    private static string MediaTypeName(int? mediaType) => mediaType switch
    {
        3 => "Hard disk (HDD)",
        4 => "Solid state drive (SSD)",
        5 => "Storage-class memory",
        _ => "Unknown type",
    };

    private static string HealthName(int? health) => health switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        _ => "Unknown",
    };

    private static Severity Max(Severity a, Severity b) => (Severity)Math.Max((int)a, (int)b);
}
