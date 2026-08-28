using System.Windows;
using SystemDiagnoseApp.ViewModels;

namespace SystemDiagnoseApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel
        {
            Confirm = (title, body) => MessageBox.Show(
                this, body, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                == MessageBoxResult.Yes,
        };

        DataContext = vm;
    }
}
