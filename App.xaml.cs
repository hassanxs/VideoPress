using System.Windows;
using System.Windows.Threading;

namespace VideoPress;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Register handlers BEFORE base.OnStartup so they catch
        // any errors during MainWindow.xaml parsing and ViewModel init
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;

        base.OnStartup(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ShowError(ex);
    }

    private static void ShowError(Exception ex)
    {
        // Walk to inner exception for more useful message
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;

        MessageBox.Show(
            $"Unexpected error:\n\n{inner.GetType().Name}:\n{inner.Message}" +
            $"\n\nStack:\n{inner.StackTrace?[..Math.Min(500, inner.StackTrace?.Length ?? 0)]}",
            "VideoPress Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
