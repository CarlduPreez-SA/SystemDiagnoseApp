using System.Text.RegularExpressions;
using SystemDiagnoseApp.Fixes;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>The "Power saver" plan throttles the CPU hard — noticeable on an already slow PC.</summary>
public sealed partial class PowerPlanCheck(ActionLog actionLog) : IDiagnosticCheck
{
    private readonly ActionLog _actionLog = actionLog;

    public string Id => "power-plan";
    public string Title => "Power plan";
    public int Order => 110;

    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    [GeneratedRegex(@"([0-9a-fA-F-]{36})\s+\((.+)\)")]
    private static partial Regex SchemeLine();

    public async Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync("powercfg", "/getactivescheme", cancellationToken).ConfigureAwait(false);
        var match = SchemeLine().Match(result.StdOut);
        if (!match.Success)
            return DiagnosticResult.Error(Id, Title, $"Could not read the active power plan.\n{result.CombinedOutput}");

        string guid = match.Groups[1].Value.ToLowerInvariant();
        string name = match.Groups[2].Value.Trim();

        bool isBattery = CimService.TryQuery("SELECT BatteryStatus FROM Win32_Battery").Count > 0;
        bool isPowerSaver = name.Contains("saver", StringComparison.OrdinalIgnoreCase);

        string detail = $"Active plan: {name}\nDevice type: {(isBattery ? "laptop (has a battery)" : "desktop (no battery)")}";

        if (!isPowerSaver)
            return DiagnosticResult.Create(Id, Title, Severity.Ok, detail + "\nThis plan does not throttle performance.");

        var severity = isBattery ? Severity.Info : Severity.Warning;
        string target = isBattery ? BalancedGuid : HighPerfGuid;
        string targetName = isBattery ? "Balanced" : "High performance";

        var fix = new DelegateFix(
            title: $"Switch to {targetName} power plan",
            confirmationText:
                $"Changes the Windows power plan from \"{name}\" to \"{targetName}\".\n\n" +
                (isBattery
                    ? "On a laptop this uses a little more battery but removes CPU throttling."
                    : "On a desktop there is no downside — Power saver only makes sense on battery.") +
                "\n\nYou can change it back any time in Control Panel → Power Options.",
            isReversible: true,
            apply: async (progress, ct) =>
            {
                var r = await ProcessRunner.RunAsync("powercfg", $"/setactive {target}", ct).ConfigureAwait(false);
                if (r.Success)
                {
                    _actionLog.Add("Power", $"Changed power plan from '{name}' to '{targetName}'.");
                    progress($"Power plan set to {targetName}.");
                    return FixOutcome.Ok($"Power plan is now {targetName}.");
                }
                return FixOutcome.Failed($"powercfg failed: {r.CombinedOutput}");
            });

        return DiagnosticResult.Create(Id, Title, severity, detail,
            $"The Power saver plan limits CPU speed. Switch to {targetName}.", [fix]);
    }
}
