using System.Windows;
using System.Windows.Threading;

namespace SystemDiagnoseApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nThe app will keep running; some results may be incomplete.",
            "System Diagnose", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
