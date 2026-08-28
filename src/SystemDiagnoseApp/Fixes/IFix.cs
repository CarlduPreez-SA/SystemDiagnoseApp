namespace SystemDiagnoseApp.Fixes;

/// <summary>Result of applying a <see cref="IFix"/>.</summary>
public sealed record FixOutcome(bool Success, string Message)
{
    public static FixOutcome Ok(string message) => new(true, message);
    public static FixOutcome Failed(string message) => new(false, message);
}

/// <summary>Streams progress text from a long-running fix back to the UI.</summary>
public delegate void FixProgress(string line);

/// <summary>
/// A guided, opt-in remediation attached to a diagnostic result. Nothing here runs
/// until the user confirms it in the UI, and every run is written to the action log.
/// </summary>
public interface IFix
{
    /// <summary>Short button label, e.g. "Disable startup item".</summary>
    string Title { get; }

    /// <summary>
    /// Full text shown in the confirmation dialog: exactly what will change, and
    /// how to undo it if possible.
    /// </summary>
    string ConfirmationText { get; }

    /// <summary>True when the change can be reversed and how is described in <see cref="ConfirmationText"/>.</summary>
    bool IsReversible { get; }

    Task<FixOutcome> ApplyAsync(FixProgress progress, CancellationToken cancellationToken);
}

/// <summary>Convenience <see cref="IFix"/> backed by a lambda.</summary>
public sealed class DelegateFix(
    string title,
    string confirmationText,
    bool isReversible,
    Func<FixProgress, CancellationToken, Task<FixOutcome>> apply) : IFix
{
    public string Title { get; } = title;
    public string ConfirmationText { get; } = confirmationText;
    public bool IsReversible { get; } = isReversible;

    public Task<FixOutcome> ApplyAsync(FixProgress progress, CancellationToken cancellationToken)
        => apply(progress, cancellationToken);
}
