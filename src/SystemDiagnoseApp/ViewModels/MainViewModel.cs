using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using SystemDiagnoseApp.Diagnostics;
using SystemDiagnoseApp.Services;

namespace SystemDiagnoseApp.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly DiagnosticRunner _runner;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource _fixCts = new();

    private string _status = "Ready. Click \"Run diagnostics\" to begin.";
    private double _progress;
    private bool _isRunning;
    private bool _hasResults;

    public MainViewModel()
    {
        ActionLog = new ActionLog();
        _runner = new DiagnosticRunner(ActionLog);

        RunCommand = new RelayCommand(RunAsync, () => !_isRunning);
        CancelCommand = new RelayCommand(Cancel, () => _isRunning);
        ExportCommand = new RelayCommand(Export, () => _hasResults);
        OpenActionLogCommand = new RelayCommand(() => { OpenFile(ActionLog.FilePath); return Task.CompletedTask; });
    }

    public ActionLog ActionLog { get; }

    /// <summary>Set by the view: shows a yes/no dialog, returns true for yes.</summary>
    public Func<string, string, bool> Confirm { get; set; } = (_, _) => false;

    public CancellationToken FixCancellation => _fixCts.Token;

    public ObservableCollection<CheckResultViewModel> Results { get; } = [];
    public ObservableCollection<string> Activity { get; } = [];

    public RelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand OpenActionLogCommand { get; }

    public string Status { get => _status; set => Set(ref _status, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            RunCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasResults
    {
        get => _hasResults;
        private set { if (Set(ref _hasResults, value)) ExportCommand.RaiseCanExecuteChanged(); }
    }

    public int CriticalCount => Results.Count(r => r.Severity == Severity.Critical && !r.IsRunning);
    public int WarningCount => Results.Count(r => r.Severity == Severity.Warning && !r.IsRunning);

    public void PostActivity(string line) => OnUi(() =>
    {
        Activity.Add(line);
        if (Activity.Count > 500) Activity.RemoveAt(0);
    });

    private async Task RunAsync()
    {
        IsRunning = true;
        HasResults = false;
        Results.Clear();
        Activity.Clear();
        Progress = 0;
        _runCts = new CancellationTokenSource();

        foreach (var (id, title) in _runner.CheckList)
            Results.Add(new CheckResultViewModel(id, title, this));

        var byId = Results.ToDictionary(r => r.CheckId);
        var progress = new Progress<CheckProgress>(p =>
        {
            Status = $"Checking: {p.Title}  ({p.Completed}/{p.Total})";
            Progress = p.Total == 0 ? 0 : 100.0 * p.Completed / p.Total;
            if (p.Result is not null && byId.TryGetValue(p.CheckId, out var vm))
            {
                vm.SetResult(p.Result);
                OnPropertyChanged(nameof(CriticalCount));
                OnPropertyChanged(nameof(WarningCount));
            }
        });

        try
        {
            var results = await _runner.RunAllAsync(progress, _runCts.Token);
            _lastResults = results;
            HasResults = true;
            Status = $"Done. {CriticalCount} critical, {WarningCount} warning(s). "
                     + "Review the results, apply fixes, then export the report.";
        }
        catch (OperationCanceledException)
        {
            Status = "Diagnostics cancelled.";
        }
        catch (Exception ex)
        {
            Status = $"Diagnostics failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            Progress = 0;
        }
    }

    private IReadOnlyList<DiagnosticResult> _lastResults = [];

    private Task Cancel()
    {
        _runCts?.Cancel();
        _fixCts.Cancel();
        _fixCts = new CancellationTokenSource();
        return Task.CompletedTask;
    }

    private Task Export()
    {
        try
        {
            string path = ReportExporter.Export(_lastResults, ActionLog);
            Status = $"Report saved to Desktop: {Path.GetFileName(path)}";
            Activity.Add($"✓ Report saved: {path}");
            OpenFile(path);
        }
        catch (Exception ex)
        {
            Status = $"Could not save the report: {ex.Message}";
        }
        return Task.CompletedTask;
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private static void OnUi(Action action)
    {
        var app = Application.Current;
        if (app is null) { action(); return; }
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.Invoke(action);
    }
}
