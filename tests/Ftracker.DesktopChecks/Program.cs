using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ftracker.Application;
using Ftracker.Desktop;
using Ftracker.Infrastructure;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        var result = 1;
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ftracker;component/Theme.xaml", UriKind.Relative) });
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timeout.Tick += (_, _) => { result = 1; timeout.Stop(); Console.WriteLine("Error: tiempo de comprobación WPF agotado."); app.Shutdown(1); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); };
        timeout.Start();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try { await CheckDesktop(); result = 0; }
            catch (Exception ex) { Console.WriteLine(ex); }
            finally { timeout.Stop(); app.Shutdown(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        }));
        Dispatcher.Run(); return result;
    }
    private static async Task CheckDesktop()
    {
        var store = new LocalStore(Path.Combine(Path.GetTempPath(), "Ftracker-ui-" + Guid.NewGuid()));
        await store.InitializeAsync(); await store.SetupAsync("Administración de prueba", "admin@example.com", "PruebaLocal12345!"); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
        await store.AddUserAsync("Prefecto de prueba", "prefecto@example.com", "PrefectoLocal123!", Roles.Prefecto);
        await store.AddRouteAsync("Edificio Á", "Prueba de interfaz", "Entrada\nPasillo");
        var window = new MainWindow(store) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2500, Top = 0 };
        window.Show(); await Task.Delay(250); window.UpdateLayout();
        var tabs = Descendants<TabControl>(window).Single();
        Check(tabs.Items.Count == 5, "Navegación de administrador");
        Check(!Descendants<System.Windows.Controls.Primitives.UniformGrid>(window).Any(), "Encabezado sin tarjetas de resumen");
        Check(!Descendants<Button>(window).Single(x => (string?)x.Content == "Reasignar").IsEnabled, "No reasigna sin seleccionar rondín");
        Check(!Descendants<Button>(window).Single(x => (string?)x.Content == "Cancelar rondín").IsEnabled, "No cancela sin seleccionar rondín");
        var userTab = tabs.Items.Cast<TabItem>().Single(x => (string)x.Header == "Usuarios"); tabs.SelectedItem = userTab; window.UpdateLayout();
        var toggle = Descendants<Button>(window).Single(x => (string?)x.Content == "Activar / desactivar");
        Check(!toggle.IsEnabled, "Acción deshabilitada sin selección");
        var grid = Descendants<DataGrid>(window).Single();
        grid.SelectedItem = ((MainViewModel)window.DataContext).Users.Single(x => x.Rol == Roles.Prefecto);
        Check(toggle.IsEnabled, "Acción habilitada para usuario seleccionado");
        grid.SelectedItem = ((MainViewModel)window.DataContext).Users.Single(x => x.Rol == Roles.Administrador);
        Check(!toggle.IsEnabled, "No permite desactivar la propia cuenta");
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(x => (string)x.Header == "Rutas y puntos"); window.UpdateLayout();
        var search = Descendants<TextBox>(window).Single(x => AutomationProperties.GetName(x).StartsWith("Buscar"));
        search.Text = "edificio a"; grid = Descendants<DataGrid>(window).Single(); Check(grid.Items.Count == 1, "Búsqueda sin distinguir acentos");
        search.Text = "no existe"; Check(grid.Items.Count == 0, "Filtro sin resultados"); search.Clear();
        var previewDir = Path.Combine(Environment.CurrentDirectory, "artifacts"); Directory.CreateDirectory(previewDir);
        SaveImage(window, Path.Combine(previewDir, "interfaz-administrador.png"));
        var editor = new Editor(window, "Validación de formulario"); var input = editor.Text("Nombre"); input.Text = "Conservar este contenido";
        Exception? dialogError = null;
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                editor.UpdateLayout();
                Descendants<Button>(editor).Single(x => (string?)x.Content == "Guardar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(30);
                Check(editor.IsVisible && input.Text == "Conservar este contenido", "Error conserva formulario y contenido");
                Check(Descendants<TextBlock>(editor).Any(x => x.Text == "Mensaje de validación de prueba"), "Error visible dentro del formulario");
                SaveImage(editor, Path.Combine(previewDir, "interfaz-formulario.png"));
            }
            catch (Exception ex) { dialogError = ex; }
            finally { editor.Close(); }
        }));
        Check(!editor.Save(() => throw new ReglaNegocioException("Mensaje de validación de prueba")), "Cancelar no guarda cambios");
        if (dialogError is not null) throw dialogError;
        Guid routeId; Guid roundId;
        await using (var db = store.Open()) { routeId = await db.Rutas.Select(x => x.Id).SingleAsync(); }
        var croquis = await CroquisWindow.ForRouteAsync(store, routeId, "Ruta de prueba");
        croquis.ShowInTaskbar = false; croquis.WindowStartupLocation = WindowStartupLocation.Manual; croquis.Left = -2500;
        croquis.Show(); croquis.UpdateLayout();
        var mapList = Descendants<ListBox>(croquis).Single();
        Check(mapList.Items.Count == 2, "Croquis incluye puntos sin ubicación");
        Check(!Descendants<Button>(croquis).Single(x => (string?)x.Content == "Guardar ubicaciones").IsEnabled, "Croquis no guarda sin cambios");
        croquis.PlaceSelectedPoint(0.3, 0.4); mapList.SelectedIndex = 1; croquis.PlaceSelectedPoint(0.7, 0.8);
        croquis.UpdateLayout();
        Check(Descendants<Button>(croquis).Count(x => AutomationProperties.GetName(x).StartsWith("Punto ")) == 2, "Marcadores visibles para puntos ubicados");
        var zoom = Descendants<Slider>(croquis).Single(); zoom.Value = 1.2; croquis.UpdateLayout();
        Descendants<Button>(croquis).Single(x => AutomationProperties.GetName(x).StartsWith("Punto 1:")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(mapList.SelectedIndex == 0, "Marcador selecciona su punto con zoom");
        Descendants<Button>(croquis).Single(x => (string?)x.Content == "Guardar ubicaciones").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Descendants<TextBlock>(croquis).Any(x => x.Text.StartsWith("Ubicaciones guardadas")));
        Check((await store.GetRouteCroquisAsync(routeId))[1].X == 0.7, "Guardar croquis persiste posiciones");
        Descendants<Button>(croquis).Single(x => (string?)x.Content == "Ajustar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SaveImage(croquis, Path.Combine(previewDir, "interfaz-croquis.png")); croquis.Close();
        await using (var db = store.Open())
        {
            var prefectId = await db.Users.Where(x => x.Rol == Roles.Prefecto).Select(x => x.Id).SingleAsync();
            roundId = (await new RondinService(db, store.Session).CrearAsync(new(routeId, prefectId, DateTime.UtcNow, null), store.Session.UserId!)).Id;
        }
        window.Close(); store.Session.SignOut(); await store.LoginAsync("prefecto@example.com", "PrefectoLocal123!");
        var prefect = new MainWindow(store) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2500, Top = 0 };
        prefect.Show(); await Task.Delay(200); prefect.UpdateLayout();
        Check(Descendants<TabControl>(prefect).Single().Items.Count == 2, "Navegación de prefecto sin catálogos administrativos");
        Check(!Descendants<Button>(prefect).Single(x => (string?)x.Content == "Iniciar rondín").IsEnabled, "No inicia sin rondín seleccionado");
        var scheduled = await CroquisWindow.ForRoundAsync(store, roundId); scheduled.ShowInTaskbar = false; scheduled.Left = -2500; scheduled.Show(); scheduled.UpdateLayout();
        Check(!Descendants<Button>(scheduled).Any(x => (string?)x.Content == "Completar punto"), "Croquis programado solo consulta"); scheduled.Close();
        await using (var db = store.Open()) { await new RondinService(db, store.Session).IniciarAsync(roundId, store.Session.UserId!); }
        var running = await CroquisWindow.ForRoundAsync(store, roundId); running.ShowInTaskbar = false; running.Left = -2500; running.Show(); running.UpdateLayout();
        Check(!Descendants<Button>(running).Any(x => (string?)x.Content == "Guardar ubicaciones"), "Prefecto no edita ubicaciones");
        var mark = Descendants<Button>(running).Single(x => (string?)x.Content == "Completar punto");
        Check(mark.IsEnabled, "Prefecto puede registrar punto pendiente desde croquis");
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<Editor>().Single(); dialog.UpdateLayout();
            Descendants<TextBox>(dialog).Single().Text = "Registro desde el croquis";
            Descendants<Button>(dialog).Single(x => (string?)x.Content == "Guardar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        mark.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Descendants<TextBlock>(running).Any(x => x.Text == "Punto registrado."));
        Check(!mark.IsEnabled, "No registra dos veces el mismo punto desde croquis");
        Check((await store.GetRoundCroquisAsync(roundId)).Puntos[0].Resultado == RegistroResultado.Completado, "Registro desde croquis persistido");
        var report = Descendants<Button>(running).Single(x => (string?)x.Content == "Reportar incidencia");
        Check(report.IsEnabled, "Puede reportar falla de punto completado mientras rondín sigue en curso");
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<Editor>().Single(); dialog.UpdateLayout();
            var fields = Descendants<TextBox>(dialog).ToList(); fields[0].Text = "Mantenimiento"; fields[1].Text = "Lámpara fundida desde croquis";
            Descendants<Button>(dialog).Single(x => (string?)x.Content == "Guardar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        report.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Descendants<TextBlock>(running).Any(x => x.Text.StartsWith("Incidencia guardada.")));
        await using (var db = store.Open())
        {
            var point = await db.RegistrosPunto.Where(x => x.RondinId == roundId).OrderBy(x => x.Orden).FirstAsync();
            Check(await db.Incidencias.AnyAsync(x => x.RondinId == roundId && x.PuntoControlId == point.PuntoControlId && x.Descripcion == "Lámpara fundida desde croquis"), "Incidencia del croquis vinculada al punto correcto");
        }
        SaveImage(running, Path.Combine(previewDir, "interfaz-croquis-prefecto.png")); running.Close();
        prefect.Close(); store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
        await store.AddUserAsync("Segundo prefecto", "segundo@example.com", "SegundoPrefecto123!", Roles.Prefecto);
        Guid scheduledId;
        await using (var db = store.Open())
        {
            var originalPrefect = await db.Users.SingleAsync(x => x.Email == "prefecto@example.com");
            scheduledId = (await new RondinService(db, store.Session).CrearAsync(new(routeId, originalPrefect.Id, DateTime.UtcNow, null), store.Session.UserId!)).Id;
        }
        var management = new MainWindow(store) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2500 };
        management.Show(); await Until(() => ((MainViewModel)management.DataContext).Rounds.Count == 2); management.UpdateLayout();
        var managementVm = (MainViewModel)management.DataContext;
        var managementTable = Descendants<DataGrid>(management).Single(); managementTable.SelectedItem = managementVm.Rounds.Single(x => x.Id == roundId);
        var cancel = Descendants<Button>(management).Single(x => (string?)x.Content == "Cancelar rondín");
        var reassign = Descendants<Button>(management).Single(x => (string?)x.Content == "Reasignar");
        Check(cancel.IsEnabled && !reassign.IsEnabled, "Rondín en curso permite cancelar pero no reasignar");
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<Editor>().Single(); dialog.UpdateLayout();
            Descendants<TextBox>(dialog).Single().Text = "Área cerrada por mantenimiento";
            Descendants<Button>(dialog).Single(x => (string?)x.Content == "Confirmar cancelación").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => managementVm.Rounds.Any(x => x.Id == roundId && x.Estado == RondinEstado.Cancelado));
        Check(!cancel.IsEnabled && !reassign.IsEnabled, "Rondín cancelado bloquea acciones administrativas repetidas");
        managementTable.SelectedItem = managementVm.Rounds.Single(x => x.Id == scheduledId);
        Check(reassign.IsEnabled, "Rondín programado habilita reasignación");
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<Editor>().Single(); dialog.UpdateLayout();
            Descendants<TextBox>(dialog).Single().Text = "Cambio de turno";
            Descendants<Button>(dialog).Single(x => (string?)x.Content == "Guardar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        reassign.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => managementVm.Rounds.Any(x => x.Id == scheduledId && x.Prefecto == "Segundo prefecto"));
        Check(managementVm.Rounds.Single(x => x.Id == scheduledId).Prefecto == "Segundo prefecto", "Reasignación desde interfaz actualiza prefecto");
        await Task.Delay(100); management.UpdateLayout();
        SaveImage(management, Path.Combine(previewDir, "interfaz-gestion-rondines.png")); management.Close();
        Console.WriteLine("COMPROBACIONES WPF COMPLETADAS");
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++) { if (condition()) return; await Task.Delay(20); }
        throw new Exception("La operación de interfaz no terminó.");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void SaveImage(Window window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            drawing.DrawRectangle(window.Background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.Fill }, null, bounds);
        }
        bitmap.Render(background);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); encoder.Save(output);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("OK: " + message); }
}
