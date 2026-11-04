using System.Windows;
using Ftracker.Application;
using Ftracker.Client;
using Ftracker.Infrastructure;

namespace Ftracker.Desktop;
public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Initialize();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var store = await CreateStoreAsync();
            var login = new AccessWindow(store, await store.NeedsSetupAsync());
            if (login.ShowDialog() != true) { Shutdown(); return; }
            var main = new MainWindow(store);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex) { MessageBox.Show("No se pudo iniciar Ftracker.\n" + ex.Message, "Ftracker", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }

    /// <summary>Con servidor configurado, todos los datos viven allá; sin él, se usa la base local de este equipo.</summary>
    private async Task<IFtrackerStore> CreateStoreAsync()
    {
        if (ServerSettings.Load() is { } server)
        {
            var remote = new RemoteStore(new Uri(server.Url), server.CertificateSha256);
            await remote.CheckServerAsync();
            remote.SessionExpired += () => (MainWindow as MainWindow)?.OnServerSessionExpired();
            return remote;
        }
        var local = new LocalStore();
        await local.InitializeAsync();
        return local;
    }
}
