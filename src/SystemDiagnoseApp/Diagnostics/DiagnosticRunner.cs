using SystemDiagnoseApp.Diagnostics.Checks;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics;

public sealed record CheckProgress(string CheckId, string Title, DiagnosticResult? Result, int Completed, int Total);

/// <summary>Owns the list of checks and runs them one at a time, reporting progress.</summary>
public sealed class DiagnosticRunner
{
    private readonly IReadOnlyList<IDiagnosticCheck> _checks;

    public DiagnosticRunner(ActionLog actionLog)
    {
        _checks =
        [
            new DiskHealthCheck(),
            new DiskSpaceCheck(),
            new StartupLoadCheck(actionLog),
            new MemoryPressureCheck(),
            new TopProcessesCheck(),
            new WindowsUpdateCheck(),
            new PendingRebootCheck(),
            new SecurityScanCheck(),
            new DriverProblemsCheck(),
            new EventLogErrorsCheck(),
            new SystemFileIntegrityCheck(actionLog),
            new PowerPlanCheck(actionLog),
            new VisualEffectsCheck(actionLog),
            new FastStartupCheck(actionLog),
            new BloatwareCheck(),
            new SystemSummaryCheck(),
        ];
    }

    public int Count => _checks.Count;

    public IEnumerable<(string Id, string Title)> CheckList =>
        _checks.OrderBy(c => c.Order).Select(c => (c.Id, c.Title));

    public async Task<IReadOnlyList<DiagnosticResult>> RunAllAsync(
        IProgress<CheckProgress> progress, CancellationToken cancellationToken)
    {
        var ordered = _checks.OrderBy(c => c.Order).ToList();
        var results = new List<DiagnosticResult>(ordered.Count);

        for (int i = 0; i < ordered.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var check = ordered[i];

            progress.Report(new CheckProgress(check.Id, check.Title, null, i, ordered.Count));

            DiagnosticResult result;
            try
            {
                result = await check.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = DiagnosticResult.Error(check.Id, check.Title,
                    $"The check failed unexpectedly: {ex.Message}");
            }

            results.Add(result);
            progress.Report(new CheckProgress(check.Id, check.Title, result, i + 1, ordered.Count));
        }

        return results;
    }
}
