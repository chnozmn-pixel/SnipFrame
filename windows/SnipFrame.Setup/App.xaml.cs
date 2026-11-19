using System.Windows;

namespace SnipFrame.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var window = new InstallWindow();
        MainWindow = window;
        window.Show();
    }
}
