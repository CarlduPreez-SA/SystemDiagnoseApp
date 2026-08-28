using Microsoft.Win32;
using SystemDiagnoseApp.Fixes;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// "Fast startup" (hybrid shutdown) keeps a stale kernel session across shutdowns.
/// On some machines it causes slow or glitchy boots and drivers that never fully reset.
/// </summary>
public sealed class FastStartupCheck(ActionLog actionLog) : IDiagnosticCheck
{
    private readonly ActionLog _actionLog = actionLog;

    public string Id => "fast-startup";
    public string Title => "Fast startup";
    public int Order => 130;

    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        int enabled;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PowerKey);
            enabled = (int)(key?.GetValue("HiberbootEnabled") ?? 1);
        }
        catch
        {
            return DiagnosticResult.Error(Id, Title, "Could not read the fast-startup setting.");
        }

        if (enabled == 0)
            return DiagnosticResult.Create(Id, Title, Severity.Ok, "Fast startup is already turned off.");

        var fix = new DelegateFix(
            title: "Turn off fast startup",
            confirmationText:
                "Disables Windows \"fast startup\". Shutdowns become true full shutdowns, so the PC starts " +
                "from a clean state every time.\n\n" +
                "Startup is a few seconds slower but more reliable. Reversible in Control Panel → Power Options → " +
                "\"Choose what the power buttons do\".",
            isReversible: true,
            apply: (progress, ct) => Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(PowerKey, true);
                    key.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
                    _actionLog.Add("Boot", "Disabled fast startup (HiberbootEnabled = 0).");
                    progress("Fast startup turned off. Applies on the next full shutdown.");
                    return FixOutcome.Ok("Fast startup is now off (takes effect after a full shut down / restart).");
                }
                catch (Exception ex)
                {
                    return FixOutcome.Failed($"Could not change the setting: {ex.Message}");
                }
            }, ct));

        return DiagnosticResult.Create(Id, Title, Severity.Info, "Fast startup is currently on.",
            "If boots are slow or the PC acts strange until a restart (not a shut down), turning fast startup off often helps.",
            [fix]);
    }
}
