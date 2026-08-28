using System.Text;
using Microsoft.Win32;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// Lists installed programs and flags the usual heavy OEM / trialware suspects that
/// tend to run background services and startup helpers on family PCs.
/// </summary>
public sealed class BloatwareCheck : IDiagnosticCheck
{
    public string Id => "bloatware";
    public string Title => "Installed programs & likely bloatware";
    public int Order => 140;

    private static readonly string[] Suspects =
    [
        "McAfee", "Norton", "Avast", "AVG", "Web Companion", "WebCompanion", "Lavasoft",
        "Wondershare", "Driver Booster", "DriverPack", "Advanced SystemCare", "IObit",
        "PC Accelerate", "PC Optimizer", "Reimage", "MyPC", "WinZip", "Search Protect",
        "Ask Toolbar", "Booking.com", "ExpressVPN", "Bonjour", "iTunes",
        "HP Support Assistant", "Dell SupportAssist", "Lenovo Vantage", "McAfee LiveSafe",
        "Candy Crush", "Spotify", "Booking", "Amazon", "Roblox", "Norton Security",
        "OneLaunch", "Wave Browser", "WaveBrowser",
    ];

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        var programs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] roots =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ];

        foreach (var (hive, name) in new[]
                 {
                     (Registry.LocalMachine, "HKLM"), (Registry.CurrentUser, "HKCU"),
                 })
        {
            foreach (var rootPath in roots)
            {
                try
                {
                    using var root = hive.OpenSubKey(rootPath);
                    if (root is null) continue;
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        using var k = root.OpenSubKey(sub);
                        string? display = k?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(display)) continue;
                        if (k?.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        programs.Add(display.Trim());
                    }
                }
                catch { /* ignore */ }
            }
        }

        if (programs.Count == 0)
            return DiagnosticResult.Error(Id, Title, "Could not read the installed-programs list.");

        var flagged = programs
            .Where(p => Suspects.Any(s => p.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var sb = new StringBuilder($"{programs.Count} installed program(s).").AppendLine();

        if (flagged.Count > 0)
        {
            sb.AppendLine().AppendLine("Worth reviewing (common background-heavy / preinstalled software):");
            foreach (var f in flagged) sb.AppendLine($"  • {f}");
        }

        sb.AppendLine().AppendLine("Full list:");
        foreach (var p in programs) sb.AppendLine($"  {p}");

        var severity = flagged.Count > 0 ? Severity.Info : Severity.Ok;
        string rec = flagged.Count > 0
            ? "Go through the flagged items with the family. Uninstall anything they don't actively use (Settings → Apps → Apps & features). Trial antivirus suites and 'PC optimizer' tools are especially worth removing. Nothing here is uninstalled automatically."
            : string.Empty;

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), rec);
    }
}
