using System.IO;

namespace SystemDiagnoseApp.Services;

public sealed record ActionLogEntry(DateTimeOffset Timestamp, string Category, string Message)
{
    public override string ToString() => $"{Timestamp:yyyy-MM-dd HH:mm:ss}  [{Category}]  {Message}";
}

/// <summary>
/// Append-only record of everything the app changed on the machine. Kept in memory
/// for the UI and the report, and mirrored to a text file on the Desktop so there is
/// a trail even if the app is closed without exporting.
/// </summary>
public sealed class ActionLog
{
    private readonly object _gate = new();
    private readonly List<ActionLogEntry> _entries = [];

    public ActionLog()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        FilePath = Path.Combine(desktop, $"SystemDiagnose-actions-{Environment.MachineName}.log");
    }

    public string FilePath { get; }

    /// <summary>Raised (on the calling thread) after an entry is added.</summary>
    public event Action<ActionLogEntry>? EntryAdded;

    public IReadOnlyList<ActionLogEntry> Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    public void Add(string category, string message)
    {
        var entry = new ActionLogEntry(DateTimeOffset.Now, category, message);
        lock (_gate)
        {
            _entries.Add(entry);
            try
            {
                File.AppendAllText(FilePath, entry + Environment.NewLine);
            }
            catch
            {
                // Logging to disk is best-effort; never let it break a fix.
            }
        }

        EntryAdded?.Invoke(entry);
    }
}
