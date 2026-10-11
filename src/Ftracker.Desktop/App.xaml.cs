using System.Windows;
using Ftracker.Infrastructure;

namespace Ftracker.Desktop;
public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var store = new LocalStore();
            await store.InitializeAsync();
            var login = new AccessWindow(store, await store.NeedsSetupAsync());
            if (login.ShowDialog() != true) { Shutdown(); return; }
            var main = new MainWindow(store);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex) { MessageBox.Show("No se pudo iniciar Ftracker.\n" + ex.Message, "Ftracker", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
}
