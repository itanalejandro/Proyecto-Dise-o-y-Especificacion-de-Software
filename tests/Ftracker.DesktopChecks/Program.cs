using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        ThemeManager.Apply(false);
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
        var themeBrush = window.Background;
        var themeToggle = ThemeManager.ToggleButton(persist: false);
        var oldThemeToggle = Descendants<Button>(window).Single(x => AutomationProperties.GetName(x) == "Cambiar a modo oscuro");
        var themeActions = (Panel)oldThemeToggle.Parent;
        var themeIndex = themeActions.Children.IndexOf(oldThemeToggle);
        themeActions.Children.RemoveAt(themeIndex);
        themeActions.Children.Insert(themeIndex, themeToggle);
        themeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        await Task.Delay(240);
        var thumb = (Border)themeToggle.Template.FindName("Thumb", themeToggle);
        Check((string)themeToggle.Content == "Oscuro" && Math.Abs(((TranslateTransform)thumb.RenderTransform).X - 22) < .1, "Interruptor oscuro muestra etiqueta y desliza la perilla");
        Check(ReferenceEquals(themeBrush, window.Background) && ((SolidColorBrush)window.Background).Color == Color.FromRgb(21, 28, 37), "Modo oscuro actualiza ventana abierta sin recrear datos");
        var themeArtifacts = Path.Combine(Environment.CurrentDirectory, "artifacts"); Directory.CreateDirectory(themeArtifacts);
        SaveImage(window, Path.Combine(themeArtifacts, "interfaz-modo-oscuro.png"));
        Check(AutomationProperties.GetName(themeToggle) == "Cambiar a modo claro", "Control de tema anuncia la siguiente acción");
        themeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        await Task.Delay(240);
        Check((string)themeToggle.Content == "Claro" && Math.Abs(((TranslateTransform)thumb.RenderTransform).X) < .1, "Interruptor claro devuelve la perilla a su posición");
        SaveImage(window, Path.Combine(themeArtifacts, "interfaz-modo-claro.png"));
        Check(((SolidColorBrush)window.Background).Color == Color.FromRgb(243, 246, 250), "Modo claro se restaura en la misma ventana");
        Check(tabs.Items.Count == 5, "Navegación de administrador");
        Check(tabs.Items.Cast<TabItem>().Select(x => (string)x.Header).SequenceEqual(new[] { "Usuarios", "Rutas y puntos", "Rondines", "Incidencias", "Bitácora" }), "Orden de navegación de administrador");
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(x => (string)x.Header == "Rondines"); window.UpdateLayout();
        Check(!Descendants<System.Windows.Controls.Primitives.UniformGrid>(window).Any(), "Encabezado sin tarjetas de resumen");
        Check(!Descendants<Button>(window).Single(x => (string?)x.Content == "Reasignar").IsEnabled, "No reasigna sin seleccionar rondín");
        Check(!Descendants<Button>(window).Single(x => (string?)x.Content == "Cancelar rondín").IsEnabled, "No cancela sin seleccionar rondín");
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(x => (string)x.Header == "Incidencias"); window.UpdateLayout();
        Check(!Descendants<Button>(window).Single(x => (string?)x.Content == "Ver evidencia").IsEnabled, "No abre evidencias sin seleccionar un reporte con archivos");
        var userTab = tabs.Items.Cast<TabItem>().Single(x => (string)x.Header == "Usuarios"); tabs.SelectedItem = userTab; window.UpdateLayout();
        var toggle = Descendants<Button>(window).Single(x => (string?)x.Content == "Activar / desactivar");
        Check(!toggle.IsEnabled, "Acción deshabilitada sin selección");
        var grid = Descendants<DataGrid>(window).Single();
        grid.SelectedItem = ((MainViewModel)window.DataContext).Users.Single(x => x.Rol == Roles.Prefecto);
        Check(toggle.IsEnabled, "Acción habilitada para usuario seleccionado");
        Check(Descendants<Button>(window).Single(x => (string?)x.Content == "Eliminar usuario").IsEnabled, "Eliminar usuario se habilita con otro usuario seleccionado");
        grid.SelectedItem = ((MainViewModel)window.DataContext).Users.Single(x => x.Rol == Roles.Administrador);
        Check(!toggle.IsEnabled, "No permite desactivar la propia cuenta");
        Check(!Descendants<Button>(window).Single(x => (string?)x.Content == "Eliminar usuario").IsEnabled, "No permite eliminar la propia cuenta");
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(x => (string)x.Header == "Rutas y puntos"); window.UpdateLayout();
        var search = Descendants<TextBox>(window).Single(x => AutomationProperties.GetName(x).StartsWith("Buscar"));
        search.Text = "edificio a"; grid = Descendants<DataGrid>(window).Single(); Check(grid.Items.Count == 1, "Búsqueda sin distinguir acentos");
        search.Text = "no existe"; Check(grid.Items.Count == 0, "Filtro sin resultados"); search.Clear();
        Check(new TimeSelector(new TimeOnly(15, 50)).Value == new TimeOnly(15, 50) && new TimeSelector(new TimeOnly(0, 5)).Value == new TimeOnly(0, 5) && new TimeSelector(new TimeOnly(12, 0)).Value == new TimeOnly(12, 0), "Selector de hora conserva horas de 12 y 24 horas");
        var previewDir = Path.Combine(Environment.CurrentDirectory, "artifacts"); Directory.CreateDirectory(previewDir);
        CheckClock(window, previewDir);
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
        await CheckEvidenceViewer(store, roundId, previewDir);
        await CheckPointEvidence(prefect, roundId, previewDir);
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
        var roundTabs = Descendants<TabControl>(management).Single();
        roundTabs.SelectedItem = roundTabs.Items.Cast<TabItem>().Single(x => (string)x.Header == "Rondines"); management.UpdateLayout();
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
        SaveImage(management, Path.Combine(previewDir, "interfaz-gestion-rondines.png"));
        var managementTabs = Descendants<TabControl>(management).Single();
        managementTabs.SelectedItem = managementTabs.Items.Cast<TabItem>().Single(x => (string)x.Header == "Incidencias"); management.UpdateLayout();
        Descendants<DataGrid>(management).Single().SelectedItem = managementVm.Incidents.Single();
        Check(Descendants<Button>(management).Single(x => (string?)x.Content == "Ver evidencia").IsEnabled, "Administrador puede abrir evidencias de rondín cancelado");
        management.Close();
        await CheckClosingDate(store, routeId);
        Console.WriteLine("COMPROBACIONES WPF COMPLETADAS");
    }
    private static async Task CheckClosingDate(LocalStore store, Guid routeId)
    {
        var vm = new MainViewModel(store); await vm.RefreshAsync();
        Check(vm.Rounds.All(x => x.CerradoEn is null && x.CerradoEl == "—"), "Programados y cancelados no muestran un cierre inventado");
        Guid id; string prefectId;
        await using (var db = store.Open())
        {
            prefectId = await db.Users.Where(x => x.Email == "prefecto@example.com").Select(x => x.Id).SingleAsync();
            id = (await new RondinService(db, store.Session).CrearAsync(new(routeId, prefectId, DateTime.UtcNow, null), store.Session.UserId!)).Id;
        }
        store.Session.SignOut(); await store.LoginAsync("prefecto@example.com", "PrefectoLocal123!");
        DateTime closing;
        await using (var db = store.Open())
        {
            var service = new RondinService(db, store.Session);
            await service.IniciarAsync(id, prefectId);
            foreach (var point in await db.RegistrosPunto.Where(x => x.RondinId == id).ToListAsync())
                await service.MarcarPuntoAsync(id, point.Id, new(RegistroResultado.Completado, null), prefectId);
            await service.CerrarAsync(id, null, prefectId);
            closing = (await db.Rondines.SingleAsync(x => x.Id == id)).CerradoEn!.Value;
        }
        await vm.RefreshAsync();
        var row = vm.Rounds.Single(x => x.Id == id);
        Check(row.CerradoEn == closing.ToLocalTime() && row.CerradoEl == closing.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), "Tabla muestra fecha y hora local del cierre guardado");
    }
    private static async Task CheckPointEvidence(MainWindow window, Guid roundId, string previewDir)
    {
        Descendants<Button>(window).Single(x => (string?)x.Content == "Actualizar datos").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Descendants<TabControl>(window).Single().IsEnabled);
        var rounds = Descendants<DataGrid>(window).Single(x => x.ItemsSource is IReadOnlyList<RoundRow>);
        rounds.SelectedItem = ((MainViewModel)window.DataContext).Rounds.Single(x => x.Id == roundId);
        await Until(() => Descendants<DataGrid>(window).Any(x => x.Items.Count == 2 && x.Items[0] is PointRow { TieneEvidencia: true }));
        window.UpdateLayout();
        var points = Descendants<DataGrid>(window).Single(x => x.Items.Count > 0 && x.Items[0] is PointRow);
        var linked = points.Items.Cast<PointRow>().Single(x => x.TieneEvidencia);
        Check(linked.IncidenciasConEvidencia.Count == 1, "Evidencia vinculada al punto y rondín correctos");
        Check(points.Items.Cast<PointRow>().Count(x => !x.TieneEvidencia) == 1, "Puntos sin adjuntos no anuncian evidencias");
        var buttons = Descendants<Button>(window).Where(x => AutomationProperties.GetName(x) == "Ver evidencias del punto").ToList();
        var button = buttons.Single(x => x.Visibility == Visibility.Visible);
        Check(Equals(button.DataContext, linked) && buttons.Any(x => x.Visibility == Visibility.Collapsed), "Botón de imagen visible solo en la fila con evidencias");
        // Opening a row must not depend on the currently selected point.
        points.SelectedItem = points.Items.Cast<PointRow>().Single(x => !x.TieneEvidencia);
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        await Until(() => System.Windows.Application.Current.Windows.OfType<EvidenceWindow>().Any());
        var viewer = System.Windows.Application.Current.Windows.OfType<EvidenceWindow>().Single();
        viewer.UpdateLayout();
        Check(Descendants<ComboBox>(viewer).Single().Items.Count == 5, "Botón del punto abre su visor sin exportar y sin depender de la selección");
        viewer.Close();
        await Until(() => Descendants<TabControl>(window).Single().IsEnabled);
        SaveImage(window, Path.Combine(previewDir, "interfaz-evidencias-punto.png"));
    }
    private static async Task CheckEvidenceViewer(LocalStore store, Guid roundId, string previewDir)
    {
        Guid incidentId;
        await using (var db = store.Open()) { incidentId = await db.Incidencias.Where(x => x.RondinId == roundId).Select(x => x.Id).SingleAsync(); }
        try { await EvidenceWindow.CreateAsync(store, incidentId); throw new Exception("Abrió visor sin adjuntos"); }
        catch (ReglaNegocioException) { Console.WriteLine("OK: Visor sin adjuntos informa validación"); }
        var pngPath = Path.Combine(store.DataDirectory, "prueba.png");
        var jpegPath = Path.Combine(store.DataDirectory, "prueba.jpg");
        var source = new BitmapImage(new Uri("pack://application:,,,/Ftracker;component/Assets/ftracker-logo.png"));
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(source));
        using (var output = File.Create(pngPath)) png.Save(output);
        var jpg = new JpegBitmapEncoder(); jpg.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0)));
        using (var output = File.Create(jpegPath)) jpg.Save(output);
        var pdfPath = Path.Combine(store.DataDirectory, "prueba.pdf"); await File.WriteAllTextAsync(pdfPath, "%PDF-1.4\n%%EOF");
        var corruptPath = Path.Combine(store.DataDirectory, "corrupta.png");
        await File.WriteAllBytesAsync(corruptPath, new byte[] {137,80,78,71,13,10,26,10,0,0,0,0});
        var missingPath = Path.Combine(store.DataDirectory, "ausente.png"); File.Copy(pngPath, missingPath);
        foreach (var path in new[] { pngPath, jpegPath, pdfPath, corruptPath, missingPath }) await store.SaveEvidenceAsync(incidentId, path);
        var attachments = await store.GetEvidenceAsync(incidentId);
        var missing = attachments.Single(x => x.NombreOriginal == "ausente.png");
        // Move only a fixture created in this isolated test directory; no user evidence is touched.
        File.Move(missing.RutaArchivo, missing.RutaArchivo + ".test-moved");
        var originalBytes = await File.ReadAllBytesAsync(attachments.Single(x => x.NombreOriginal == "prueba.png").RutaArchivo);
        var fileCount = Directory.GetFiles(Path.Combine(store.DataDirectory, "evidencias")).Length;
        var viewer = await EvidenceWindow.CreateAsync(store, incidentId);
        viewer.ShowInTaskbar = false; viewer.WindowStartupLocation = WindowStartupLocation.Manual; viewer.Left = -2500;
        viewer.Show(); viewer.UpdateLayout();
        var choice = Descendants<ComboBox>(viewer).Single();
        var image = Descendants<Image>(viewer).Single(x => AutomationProperties.GetName(x) == "Vista previa de evidencia");
        await Until(() => choice.IsEnabled && image.Source is not null);
        Check(choice.Items.Count == 5 && image.Source is BitmapSource, "Visor muestra PNG y lista de adjuntos");
        var zoom = Descendants<Slider>(viewer).Single(); zoom.Value = 1.5;
        Check(image.LayoutTransform is ScaleTransform { ScaleX: 1.5 }, "Zoom interno cambia escala de imagen");
        Descendants<Button>(viewer).Single(x => (string?)x.Content == "Ajustar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SaveImage(viewer, Path.Combine(previewDir, "interfaz-evidencias.png"));
        using (var unlocked = new FileStream(attachments.Single(x => x.NombreOriginal == "prueba.png").RutaArchivo, FileMode.Open, FileAccess.Read, FileShare.None))
            Check(unlocked.Length == originalBytes.Length, "Visor no mantiene bloqueado archivo original");
        choice.SelectedItem = choice.Items.Cast<Ftracker.Domain.Evidencia>().Single(x => x.NombreOriginal == "prueba.jpg");
        await Until(() => choice.IsEnabled && image.Source is not null);
        Check(image.Source is BitmapSource, "Visor muestra JPEG");
        choice.SelectedItem = choice.Items.Cast<Ftracker.Domain.Evidencia>().Single(x => x.NombreOriginal == "prueba.pdf");
        await Until(() => choice.IsEnabled && Descendants<TextBlock>(viewer).Any(x => x.Text.StartsWith("La vista previa de PDF")));
        Check(image.Source is null && !zoom.IsEnabled, "PDF informa exportación sin mostrar imagen anterior");
        choice.SelectedItem = choice.Items.Cast<Ftracker.Domain.Evidencia>().Single(x => x.NombreOriginal == "corrupta.png");
        await Until(() => choice.IsEnabled && Descendants<TextBlock>(viewer).Any(x => x.Text.StartsWith("No se pudo mostrar la imagen")));
        Check(image.Source is null, "Imagen dañada informa error sin cerrar visor");
        choice.SelectedItem = choice.Items.Cast<Ftracker.Domain.Evidencia>().Single(x => x.NombreOriginal == "ausente.png");
        await Until(() => choice.IsEnabled && Descendants<TextBlock>(viewer).Any(x => x.Text.StartsWith("El archivo ya no está disponible")));
        Check(viewer.IsVisible, "Archivo ausente no cierra visor");
        choice.SelectedItem = choice.Items.Cast<Ftracker.Domain.Evidencia>().Single(x => x.NombreOriginal == "prueba.png");
        await Until(() => choice.IsEnabled && image.Source is not null);
        Check((await File.ReadAllBytesAsync(attachments.Single(x => x.NombreOriginal == "prueba.png").RutaArchivo)).SequenceEqual(originalBytes) &&
            Directory.GetFiles(Path.Combine(store.DataDirectory, "evidencias")).Length == fileCount, "Vista previa no altera archivos ni crea copias");
        viewer.Close();
    }
    private static void CheckClock(Window owner, string previewDir)
    {
        var clock = new ClockTimeWindow(new TimeOnly(15, 35), new DateTime(2026, 10, 11))
        { Owner = owner, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2500, Top = 0 };
        clock.Show(); clock.UpdateLayout();
        ThemeManager.Apply(true); clock.UpdateLayout();
        SaveImage(clock, Path.Combine(previewDir, "interfaz-reloj-oscuro.png"));
        ThemeManager.Apply(false);
        var dial = Descendants<ClockDial>(clock).Single();
        Check(clock.Value == new TimeOnly(15, 35), "Reloj conserva hora inicial y periodo p. m.");
        var center = new Point(dial.ActualWidth / 2, dial.ActualHeight / 2);
        dial.Part = ClockPart.Hours; dial.SelectAt(new Point(center.X, 25));
        Check(clock.Value == new TimeOnly(12, 35), "Seleccionar 12 p. m. conserva mediodía");
        dial.FinishSelection(); Check(dial.Part == ClockPart.Minutes, "Tras elegir hora se seleccionan minutos");
        dial.SelectAt(new Point(center.X + 110, center.Y)); Check(clock.Value == new TimeOnly(12, 15), "Reloj marca minutos de cinco en cinco");
        dial.SelectAt(new Point(center.X, 25)); Check(clock.Value.Minute == 0, "Parte superior del reloj corresponde al minuto cero");
        Descendants<Button>(clock).Single(x => (string?)x.Content == "a. m.").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(clock.Value == new TimeOnly(0, 0), "Seleccionar 12 a. m. corresponde a medianoche");
        Descendants<Button>(clock).Single(x => (string?)x.Content == "−1 min").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(clock.Value == new TimeOnly(23, 59), "Ajuste exacto de minutos cruza medianoche sin cambiar fecha seleccionada");
        var before = clock.Value;
        Check(!dial.SelectAt(center) && !dial.SelectAt(new Point(-100, -100)) && clock.Value == before, "Centro y puntos fuera del reloj no cambian la hora");
        dial.SetValue(new TimeOnly(17, 35));
        Check(Descendants<TextBlock>(clock).Any(x => x.Text == "5:35 p. m.") && Descendants<TextBlock>(clock).Any(x => x.Text.Contains("octubre") && x.Text.Contains("2026")), "Reloj muestra fecha y hora elegidas a la derecha");
        dial.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(clock)!, Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        Check(clock.Value == new TimeOnly(17, 36), "Flechas del reloj permiten minutos exactos");
        dial.SetValue(new TimeOnly(17, 35)); dial.Part = ClockPart.Hours; SaveImage(clock, Path.Combine(previewDir, "interfaz-reloj-horas.png"));
        dial.Part = ClockPart.Minutes; SaveImage(clock, Path.Combine(previewDir, "interfaz-reloj-minutos.png")); clock.Close();
        var editor = new Editor(owner, "Programar recorrido") { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2500 };
        var (date, selector) = editor.Schedule(); editor.Show(); editor.UpdateLayout(); date.SelectedDate = new DateTime(2026, 10, 12);
        var input = Descendants<TextBox>(selector).Single(); input.Text = "17:37";
        Check(selector.Value == new TimeOnly(17, 37) && Descendants<TextBlock>(selector).Any(x => x.Text == "12/10/2026 · 5:37 p. m."), "Captura exacta sincroniza fecha y hora del formulario");
        input.Text = "25:90"; var rejected = false; try { _ = selector.Value; } catch (ReglaNegocioException) { rejected = true; }
        Check(rejected, "Hora escrita inválida bloquea guardado"); input.Text = "17:37";
        foreach (var accept in new[] { false, true })
        {
            Exception? modalError = null;
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var modal = System.Windows.Application.Current.Windows.OfType<ClockTimeWindow>().Single();
                try
                {
                    modal.Left = -2500; Descendants<ClockDial>(modal).Single().SetValue(new TimeOnly(20, 45));
                    Descendants<Button>(modal).Single(x => (string?)x.Content == (accept ? "Usar esta hora" : "Cancelar")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { modalError = ex; modal.Close(); }
            }));
            Descendants<Button>(selector).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (modalError is not null) throw modalError;
            Check(selector.Value == (accept ? new TimeOnly(20, 45) : new TimeOnly(17, 37)), accept ? "Aceptar reloj actualiza hora del formulario" : "Cancelar reloj conserva la hora previa");
        }
        date.SelectedDate = null; Check(Descendants<TextBlock>(selector).Any(x => x.Text.StartsWith("Sin fecha")), "Sin fecha el formulario no muestra una programación engañosa");
        date.SelectedDate = new DateTime(2026, 10, 12); SaveImage(editor, Path.Combine(previewDir, "interfaz-programacion.png")); editor.Close();
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
