namespace SystemDiagnoseApp.Diagnostics;

/// <summary>How concerning a diagnostic result is. Higher = worse.</summary>
public enum Severity
{
    /// <summary>Nothing wrong / informational context.</summary>
    Ok = 0,

    /// <summary>Worth knowing, not itself a problem.</summary>
    Info = 1,

    /// <summary>Likely contributes to the slowness; should be addressed.</summary>
    Warning = 2,

    /// <summary>Very likely a major cause; address as soon as possible.</summary>
    Critical = 3,

    /// <summary>The check could not run (missing data, access denied, etc.).</summary>
    Unknown = 4,
}

public static class SeverityExtensions
{
    /// <summary>Returns the more concerning of two severities (Unknown never wins over a real finding).</summary>
    public static Severity Worse(this Severity a, Severity b)
    {
        if (a == Severity.Unknown) return b;
        if (b == Severity.Unknown) return a;
        return (int)a >= (int)b ? a : b;
    }
}
