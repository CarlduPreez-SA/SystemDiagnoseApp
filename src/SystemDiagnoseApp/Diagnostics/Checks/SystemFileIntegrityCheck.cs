using SystemDiagnoseApp.Fixes;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// Corrupted Windows system files cause odd slowness and errors. There is no fast
/// read-only check, so this always offers the standard repair tools as opt-in fixes.
/// </summary>
public sealed class SystemFileIntegrityCheck(ActionLog actionLog) : IDiagnosticCheck
{
    private readonly ActionLog _actionLog = actionLog;

    public string Id => "system-file-integrity";
    public string Title => "Windows system files";
    public int Order => 105;

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
    {
        var sfc = new DelegateFix(
            title: "Run System File Checker (sfc /scannow)",
            confirmationText:
                "Runs 'sfc /scannow', which scans all protected Windows files and repairs any that are " +
                "corrupted from a local cache.\n\n" +
                "Safe to run. It is SLOW (10–40 minutes) and uses the disk heavily while it runs. " +
                "Progress is shown below; leave the app open until it finishes.",
            isReversible: false,
            apply: async (progress, ct) =>
            {
                _actionLog.Add("Repair", "Started sfc /scannow.");
                var r = await ProcessRunner.RunStreamingAsync("sfc", "/scannow", line => progress(line), ct).ConfigureAwait(false);
                _actionLog.Add("Repair", $"sfc /scannow finished (exit {r.ExitCode}).");
                return r.ExitCode == 0
                    ? FixOutcome.Ok("System File Checker completed. Review the messages above; a restart is wise if it repaired anything.")
                    : FixOutcome.Failed($"sfc exited with code {r.ExitCode}. If it reports it could not fix everything, run the DISM repair next, then sfc again.");
            });

        var dism = new DelegateFix(
            title: "Repair the Windows image (DISM RestoreHealth)",
            confirmationText:
                "Runs 'DISM /Online /Cleanup-Image /RestoreHealth', which repairs the underlying Windows " +
                "component store that sfc relies on. Run this if sfc could not fix everything.\n\n" +
                "Safe, but SLOW (can exceed 30 minutes) and needs a working internet connection to download " +
                "replacement files from Windows Update.",
            isReversible: false,
            apply: async (progress, ct) =>
            {
                _actionLog.Add("Repair", "Started DISM RestoreHealth.");
                var r = await ProcessRunner.RunStreamingAsync(
                    "DISM", "/Online /Cleanup-Image /RestoreHealth", line => progress(line), ct).ConfigureAwait(false);
                _actionLog.Add("Repair", $"DISM RestoreHealth finished (exit {r.ExitCode}).");
                return r.ExitCode == 0
                    ? FixOutcome.Ok("DISM completed. Now run System File Checker again to repair from the restored store.")
                    : FixOutcome.Failed($"DISM exited with code {r.ExitCode}. Check the internet connection and try again.");
            });

        var result = DiagnosticResult.Create(Id, Title, Severity.Info,
            "No quick test exists for system-file corruption. If the PC has errors, crashes, or unexplained " +
            "slowness, run the two repair tools below in order (SFC first, then DISM if SFC can't fix everything, then SFC again).",
            "Optional. Run these if other checks didn't explain the slowness, or the PC shows errors/crashes.",
            [sfc, dism]);

        return Task.FromResult(result);
    }
}
