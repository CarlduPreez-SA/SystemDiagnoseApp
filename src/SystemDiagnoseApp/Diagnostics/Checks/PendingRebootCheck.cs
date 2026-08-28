using System.Text;
using Microsoft.Win32;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>A deferred reboot leaves Windows half-patched and often sluggish until it happens.</summary>
public sealed class PendingRebootCheck : IDiagnosticCheck
{
    public string Id => "pending-reboot";
    public string Title => "Restart needed";
    public int Order => 70;

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        var reasons = new List<string>();

        if (KeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
            reasons.Add("Windows servicing (Component Based Servicing) is waiting to finish.");

        if (KeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
            reasons.Add("Windows Update has installed updates that need a restart.");

        try
        {
            using var sm = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            if (sm?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 })
                reasons.Add("Files are queued to be replaced on the next restart.");
        }
        catch { /* ignore */ }

        try
        {
            using var cn = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Netlogon");
            if (cn?.GetValue("JoinDomain") is not null || cn?.GetValue("AvoidSpnSet") is not null)
                reasons.Add("A domain-join operation is pending.");
        }
        catch { /* ignore */ }

        if (reasons.Count == 0)
            return DiagnosticResult.Create(Id, Title, Severity.Ok, "No pending restart detected.");

        var sb = new StringBuilder("Windows is waiting for a restart:").AppendLine();
        foreach (var r in reasons) sb.AppendLine($"  • {r}");

        return DiagnosticResult.Create(Id, Title, Severity.Warning, sb.ToString().TrimEnd(),
            "Save any open work and restart the PC (Start → Power → Restart, not Shut down). Then re-run these checks.");
    }

    private static bool KeyExists(RegistryKey hive, string path)
    {
        try { using var k = hive.OpenSubKey(path); return k is not null; }
        catch { return false; }
    }
}
