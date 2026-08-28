using System.Collections.ObjectModel;
using System.Windows.Media;
using SystemDiagnoseApp.Diagnostics;
using SystemDiagnoseApp.Fixes;

namespace SystemDiagnoseApp.ViewModels;

public sealed class CheckResultViewModel : ObservableObject
{
    private DiagnosticResult? _result;
    private bool _isRunning = true;

    public CheckResultViewModel(string checkId, string title, MainViewModel owner)
    {
        CheckId = checkId;
        Title = title;
        Owner = owner;
    }

    public string CheckId { get; }
    public string Title { get; }
    public MainViewModel Owner { get; }

    public ObservableCollection<FixViewModel> Fixes { get; } = [];

    public bool IsRunning
    {
        get => _isRunning;
        private set { if (Set(ref _isRunning, value)) OnPropertyChanged(nameof(StatusText)); }
    }

    public DiagnosticResult? Result
    {
        get => _result;
        private set
        {
            if (!Set(ref _result, value)) return;
            OnPropertyChanged(nameof(Severity));
            OnPropertyChanged(nameof(SeverityText));
            OnPropertyChanged(nameof(SeverityBrush));
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(Recommendation));
            OnPropertyChanged(nameof(HasRecommendation));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public void SetResult(DiagnosticResult result)
    {
        Result = result;
        IsRunning = false;
        Fixes.Clear();
        foreach (var fix in result.Fixes)
            Fixes.Add(new FixViewModel(fix, Owner));
    }

    public Severity Severity => _result?.Severity ?? Severity.Unknown;
    public string SeverityText => IsRunning ? "…" : Severity.ToString().ToUpperInvariant();
    public string Detail => _result?.Detail ?? string.Empty;
    public string Recommendation => _result?.Recommendation ?? string.Empty;
    public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);
    public string StatusText => IsRunning ? "Checking…" : Severity.ToString();

    public Brush SeverityBrush => Severity switch
    {
        Severity.Ok => new SolidColorBrush(Color.FromRgb(0x2e, 0x7d, 0x32)),
        Severity.Info => new SolidColorBrush(Color.FromRgb(0x02, 0x77, 0xbd)),
        Severity.Warning => new SolidColorBrush(Color.FromRgb(0xef, 0x6c, 0x00)),
        Severity.Critical => new SolidColorBrush(Color.FromRgb(0xc6, 0x28, 0x28)),
        _ => new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)),
    };
}

public sealed class FixViewModel : ObservableObject
{
    private readonly IFix _fix;
    private readonly MainViewModel _owner;
    private string _status = "";
    private bool _applied;

    public FixViewModel(IFix fix, MainViewModel owner)
    {
        _fix = fix;
        _owner = owner;
        ApplyCommand = new RelayCommand(ApplyAsync, () => !_applied);
    }

    public string Title => _fix.Title;
    public RelayCommand ApplyCommand { get; }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool Applied
    {
        get => _applied;
        private set { if (Set(ref _applied, value)) ApplyCommand.RaiseCanExecuteChanged(); }
    }

    private async Task ApplyAsync()
    {
        string body = _fix.ConfirmationText +
                      (_fix.IsReversible ? "\n\nThis change can be undone." : "\n\nThis change cannot be automatically undone.");

        if (!_owner.Confirm($"Apply: {_fix.Title}", body))
            return;

        Status = "Running…";
        _owner.Activity.Add($"▶ {_fix.Title}");

        FixOutcome outcome;
        try
        {
            outcome = await _fix.ApplyAsync(line => _owner.PostActivity($"   {line}"), _owner.FixCancellation);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
            _owner.Activity.Add($"■ {_fix.Title} cancelled");
            return;
        }
        catch (Exception ex)
        {
            outcome = FixOutcome.Failed(ex.Message);
        }

        Status = outcome.Success ? "Done ✓" : "Failed ✗";
        Applied = outcome.Success;
        _owner.Activity.Add($"{(outcome.Success ? "✓" : "✗")} {_fix.Title}: {outcome.Message}");
        _owner.ActionLog.Add("Fix", $"{_fix.Title} — {(outcome.Success ? "OK" : "FAILED")}: {outcome.Message}");
    }
}
