namespace SystemDiagnoseApp.Diagnostics;

/// <summary>
/// One self-contained diagnostic. Implementations must not throw: catch their own
/// errors and return <see cref="DiagnosticResult.Error"/> instead.
/// </summary>
public interface IDiagnosticCheck
{
    /// <summary>Stable identifier, used in the exported report and action log.</summary>
    string Id { get; }

    /// <summary>Display name.</summary>
    string Title { get; }

    /// <summary>Lower runs first. Roughly "how often this explains the slowness".</summary>
    int Order { get; }

    Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken);
}
