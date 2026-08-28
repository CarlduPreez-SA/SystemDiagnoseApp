using System.IO;
using System.Text;
using Microsoft.Win32;
using SystemDiagnoseApp.Fixes;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.Diagnostics.Checks;

/// <summary>
/// Everything that launches at sign-in competes for the disk and CPU exactly when
/// the user is trying to open their first app. A long list is a classic cause of
/// "it takes forever after I log in".
/// </summary>
public sealed class StartupLoadCheck(ActionLog actionLog) : IDiagnosticCheck
{
    private readonly ActionLog _actionLog = actionLog;

    public string Id => "startup-load";
    public string Title => "Programs that start automatically";
    public int Order => 30;

    private sealed record StartupItem(string Name, string Command, string Source, bool Enabled, IFix? DisableFix);

    public Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken)
        => Task.Run(Collect, cancellationToken);

    private DiagnosticResult Collect()
    {
        var items = new List<StartupItem>();
        items.AddRange(FromRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run (this user)", "Run"));
        items.AddRange(FromRunKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run (all users)", "Run"));
        items.AddRange(FromRunKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run (all users, 32-bit)", "Run"));
        items.AddRange(FromStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup folder (this user)"));
        items.AddRange(FromStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Startup folder (all users)"));

        // Scheduled-task logon triggers and other autostarts, via WMI.
        foreach (var row in CimService.TryQuery("SELECT Name, Command, Location, User FROM Win32_StartupCommand"))
        {
            string name = row.GetString("Name") ?? "(unnamed)";
            if (items.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
            items.Add(new StartupItem(name, row.GetString("Command") ?? "", row.GetString("Location") ?? "other", true, null));
        }

        int enabledCount = items.Count(i => i.Enabled);

        var sb = new StringBuilder();
        sb.AppendLine($"{enabledCount} program(s) set to start automatically:");
        foreach (var item in items.OrderByDescending(i => i.Enabled).ThenBy(i => i.Name))
        {
            sb.AppendLine($"  {(item.Enabled ? "[on] " : "[off]")} {item.Name}  —  {item.Source}");
            if (!string.IsNullOrWhiteSpace(item.Command))
                sb.AppendLine($"         {Trim(item.Command)}");
        }

        var severity = enabledCount switch
        {
            >= 12 => Severity.Warning,
            >= 7 => Severity.Info,
            _ => Severity.Ok,
        };

        string recommendation = severity >= Severity.Info
            ? "Review the list. Disable anything they don't use every session (updaters, chat apps, printer/audio helper tools, game launchers). Keep antivirus and OneDrive if they use it. Use the 'Disable' buttons below, or Task Manager → Startup."
            : string.Empty;

        var fixes = items
            .Where(i => i is { Enabled: true, DisableFix: not null })
            .Select(i => i.DisableFix!)
            .ToList();

        return DiagnosticResult.Create(Id, Title, severity, sb.ToString().TrimEnd(), recommendation, fixes);
    }

    private IEnumerable<StartupItem> FromRunKey(RegistryKey hive, string subPath, string source, string approvedKind)
    {
        using var key = hive.OpenSubKey(subPath);
        if (key is null) yield break;

        bool isMachine = hive.Name.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);

        foreach (var valueName in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(valueName)) continue;
            string command = key.GetValue(valueName)?.ToString() ?? "";
            bool enabled = IsApprovedEnabled(isMachine, approvedKind, valueName);

            var fix = new DelegateFix(
                title: $"Disable startup: {valueName}",
                confirmationText:
                    $"Stops \"{valueName}\" from launching automatically when Windows starts.\n\n" +
                    $"Command: {command}\n\n" +
                    "The program itself is NOT uninstalled and can still be opened manually. " +
                    "This mirrors what Task Manager's Startup tab does, and can be undone there.",
                isReversible: true,
                apply: (progress, ct) => Task.Run(() =>
                {
                    try
                    {
                        SetApproved(isMachine, approvedKind, valueName, enable: false);
                        progress($"Disabled '{valueName}'.");
                        _actionLog.Add("Startup", $"Disabled auto-start entry '{valueName}' ({source}).");
                        return FixOutcome.Ok($"'{valueName}' will no longer start automatically.");
                    }
                    catch (Exception ex)
                    {
                        return FixOutcome.Failed($"Could not disable '{valueName}': {ex.Message}");
                    }
                }, ct));

            yield return new StartupItem(valueName, command, source, enabled, fix);
        }
    }

    private IEnumerable<StartupItem> FromStartupFolder(string folder, string source)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) yield break;

        foreach (var path in Directory.EnumerateFiles(folder))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(Path.GetFileName(path), "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

            string capturedPath = path;
            var fix = new DelegateFix(
                title: $"Disable startup: {name}",
                confirmationText:
                    $"Moves the shortcut \"{Path.GetFileName(path)}\" out of the Startup folder so it no longer " +
                    "launches at sign-in.\n\nThe file is moved to a 'DisabledStartup' backup folder (not deleted), " +
                    "so it can be restored.",
                isReversible: true,
                apply: (progress, ct) => Task.Run(() =>
                {
                    try
                    {
                        string backupDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "SystemDiagnoseApp", "DisabledStartup");
                        Directory.CreateDirectory(backupDir);
                        string dest = Path.Combine(backupDir, Path.GetFileName(capturedPath));
                        File.Move(capturedPath, dest, overwrite: true);
                        progress($"Moved '{Path.GetFileName(capturedPath)}' to {backupDir}");
                        _actionLog.Add("Startup", $"Moved startup shortcut '{Path.GetFileName(capturedPath)}' to {backupDir}.");
                        return FixOutcome.Ok($"'{name}' will no longer start automatically. Backup: {dest}");
                    }
                    catch (Exception ex)
                    {
                        return FixOutcome.Failed($"Could not move '{name}': {ex.Message}");
                    }
                }, ct));

            yield return new StartupItem(name, capturedPath, source, true, fix);
        }
    }

    private static string ApprovedPath(string kind) =>
        $@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{kind}";

    private static bool IsApprovedEnabled(bool machine, string kind, string valueName)
    {
        try
        {
            using var root = machine ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(ApprovedPath(kind));
            if (key?.GetValue(valueName) is byte[] data && data.Length > 0)
                return (data[0] & 0x01) == 0; // bit 0 set => disabled
        }
        catch
        {
            // treat as enabled if we can't tell
        }
        return true;
    }

    private static void SetApproved(bool machine, string kind, string valueName, bool enable)
    {
        using var root = machine ? Registry.LocalMachine : Registry.CurrentUser;
        using var key = root.CreateSubKey(ApprovedPath(kind), writable: true);
        var data = new byte[12];
        data[0] = (byte)(enable ? 0x02 : 0x03);
        if (!enable)
        {
            long now = DateTime.UtcNow.ToFileTimeUtc();
            BitConverter.GetBytes(now).CopyTo(data, 4);
        }
        key.SetValue(valueName, data, RegistryValueKind.Binary);
    }

    private static string Trim(string s) => s.Length <= 110 ? s : s[..107] + "...";
}
