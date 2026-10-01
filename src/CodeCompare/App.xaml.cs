using System.Windows;
using System.Windows.Threading;

namespace CodeCompare;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // CodeCompare.exe "C:\left" "C:\right" starts a comparison immediately.
        if (e.Args.Length >= 2)
            window.StartComparison(e.Args[0], e.Args[1]);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow, e.Exception.Message, "Code Compare – unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
