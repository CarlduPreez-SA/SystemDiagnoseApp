using Microsoft.Win32;
using SystemDiagnoseApp.Fixes;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// Animations and shadows cost very little on modern hardware but are visibly slow
/// on an old PC. Switching to "best performance" is a safe, instant win there.
/// </summary>
public sealed class VisualEffectsCheck(ActionLog actionLog) : IDiagnosticCheck
{
    private readonly ActionLog _actionLog = actionLog;

    public string Id => "visual-effects";
    public string Title => "Visual effects";
    public int Order => 120;

    private const string VfxKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Run, cancellationToken);

    private DiagnosticResult Run()
    {
        int setting;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(VfxKey);
            setting = (int)(key?.GetValue("VisualFXSetting") ?? 0);
        }
        catch
        {
            return DiagnosticResult.Error(Id, Title, "Could not read the visual-effects setting.");
        }

        string name = setting switch
        {
            1 => "Adjust for best appearance",
            2 => "Adjust for best performance",
            3 => "Custom",
            _ => "Let Windows choose (default)",
        };

        if (setting == 2)
            return DiagnosticResult.Create(Id, Title, Severity.Ok, $"Already set to: {name}.");

        var fix = new DelegateFix(
            title: "Set visual effects to best performance",
            confirmationText:
                "Turns off window animations, menu fades, shadows and the like (System Properties → " +
                "Performance → \"Adjust for best performance\").\n\n" +
                "Purely cosmetic — no features are lost. Reversible in the same dialog. " +
                "Takes effect after signing out and back in.",
            isReversible: true,
            apply: (progress, ct) => Task.Run(() =>
            {
                try
                {
                    using (var vfx = Registry.CurrentUser.CreateSubKey(VfxKey, true))
                        vfx.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord);

                    // Match what the Performance Options dialog writes.
                    using (var deskKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true))
                    {
                        deskKey.SetValue("UserPreferencesMask",
                            new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary);
                        deskKey.SetValue("DragFullWindows", "0", RegistryValueKind.String);
                    }
                    using (var wm = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM", true))
                        wm.SetValue("EnableAeroPeek", 0, RegistryValueKind.DWord);
                    using (var adv = Registry.CurrentUser.CreateSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true))
                        adv.SetValue("ListviewShadow", 0, RegistryValueKind.DWord);

                    _actionLog.Add("Visuals", "Set visual effects to 'best performance'.");
                    progress("Visual effects set to best performance. Sign out and back in to see the full effect.");
                    return FixOutcome.Ok("Visual effects set to best performance (sign out/in to apply fully).");
                }
                catch (Exception ex)
                {
                    return FixOutcome.Failed($"Could not change visual effects: {ex.Message}");
                }
            }, ct));

        return DiagnosticResult.Create(Id, Title, Severity.Info, $"Current setting: {name}.",
            "On an older PC, switching to 'best performance' makes the desktop feel snappier.", [fix]);
    }
}
