using SystemDiagnoseApp.Fixes;

namespace SystemDiagnoseApp.Diagnostics;

/// <summary>Outcome of a single <see cref="IDiagnosticCheck"/>.</summary>
public sealed class DiagnosticResult
{
    public required string CheckId { get; init; }

    /// <summary>Human-readable check name, e.g. "Disk type &amp; health".</summary>
    public required string Title { get; init; }

    public required Severity Severity { get; init; }

    /// <summary>What was found, in plain language. May contain multiple lines.</summary>
    public required string Detail { get; init; }

    /// <summary>What to do about it. Empty when nothing is needed.</summary>
    public string Recommendation { get; init; } = string.Empty;

    /// <summary>Optional guided fixes the user can choose to apply.</summary>
    public IReadOnlyList<IFix> Fixes { get; init; } = [];

    public static DiagnosticResult Create(
        string checkId, string title, Severity severity, string detail,
        string recommendation = "", IReadOnlyList<IFix>? fixes = null)
        => new()
        {
            CheckId = checkId,
            Title = title,
            Severity = severity,
            Detail = detail,
            Recommendation = recommendation,
            Fixes = fixes ?? [],
        };

    public static DiagnosticResult Error(string checkId, string title, string detail)
        => Create(checkId, title, Severity.Unknown, detail,
            "This check could not complete. It may need more privileges or the data is unavailable on this PC.");
}
