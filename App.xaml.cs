using AccountManager.Services;
using System.Windows;
using System.Windows.Threading;

namespace AccountManager;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        base.OnStartup(e);

        var database = new AccountDatabase();
        var security = new SecurityService(database);

        if (!security.IsConfigured)
        {
            if (!RunInitialSetup(security))
            {
                Shutdown();
                return;
            }
        }
        else
        {
            var unlock = new UnlockWindow(security);
            if (unlock.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        database.Protector = security;
        var mainWindow = new MainWindow(database, security);
        MainWindow = mainWindow;
        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    private static bool RunInitialSetup(SecurityService security)
    {
        while (true)
        {
            var setup = new SecuritySetupWindow();
            if (setup.ShowDialog() != true) return false;

            try
            {
                security.SetupNewVault(setup.MasterPassword, setup.PasswordHint, setup.RecoveryQuestion, setup.RecoveryAnswer);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(setup, ex.Message, "设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "程序错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
