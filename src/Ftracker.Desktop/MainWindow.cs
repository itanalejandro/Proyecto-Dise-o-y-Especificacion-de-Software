using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace Ftracker.Desktop;
public sealed class MainWindow : Window
{
    private readonly LocalStore store;
    private readonly MainViewModel vm;
    private readonly TabControl tabs = new();
    private readonly DataGrid users = Table(("Nombre", "Nombre"), ("Correo", "Correo"), ("Rol", "Rol"), ("Activo", "Activo"));
    private readonly DataGrid routes = Table(("Ruta", "Nombre"), ("Puntos", "Puntos"), ("Activa", "Activa"));
    private readonly DataGrid rounds = Table(("Ruta", "Ruta"), ("Prefecto", "Prefecto"), ("Programado", "Programado"), ("Estado", "Estado"));
    private readonly DataGrid points = Table(("Punto", "Nombre"), ("Resultado", "Resultado"), ("Observación", "Observaciones"));
    private readonly DataGrid incidents = Table(("Ruta", "Ruta"), ("Tipo", "Tipo"), ("Descripción", "Descripcion"), ("Prioridad", "Prioridad"), ("Estado", "Estado"), ("Evidencias", "Archivos"));
    private readonly TextBlock status = new() { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly List<(Button Button, Func<bool> Enabled)> contextualActions = [];
    private readonly List<(DataGrid Grid, TextBox Search)> searches = [];
    private bool busy;
    public MainWindow(LocalStore store)
    {
        this.store = store; vm = new(store); DataContext = vm;
        Style = (Style)FindResource(typeof(Window));
        Title = "Ftracker"; Width = 1180; Height = 800; MinWidth = 900; MinHeight = 650; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Brand.Icon;
        Background = new SolidColorBrush(Color.FromRgb(245, 248, 251));
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Brand.Header(30));
        header.Children.Add(new TextBlock { Text = $"{store.Session.User!.NombreCompleto} · {store.Session.Role}", Margin = new Thickness(0, 4, 0, 12) });
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 16) };
        foreach (var card in new[] { ("Programados", nameof(vm.Programados)), ("En curso", nameof(vm.EnCurso)), ("Cerrados", nameof(vm.Cerrados)), ("Incidencias abiertas", nameof(vm.Abiertas)) })
        {
            var content = new StackPanel(); var count = new TextBlock { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.SteelBlue }; count.SetBinding(TextBlock.TextProperty, new Binding(card.Item2)); content.Children.Add(count); content.Children.Add(new TextBlock { Text = card.Item1, Foreground = Brushes.SlateGray });
            cards.Children.Add(new Border { Child = content, Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(0, 0, 10, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(223,231,239)), BorderThickness = new Thickness(1) });
        }
        header.Children.Add(cards);
        var actions = new WrapPanel(); var update = ActionButton("Actualizar datos", Refresh); update.Style = (Style)FindResource("SecondaryButton"); actions.Children.Add(update); var logout = ActionButton("Cerrar sesión", Logout); logout.Style = (Style)FindResource("SecondaryButton"); actions.Children.Add(logout); header.Children.Add(actions);
        status.Foreground = Brushes.SlateGray; status.FontSize = 12;
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(tabs);
        BuildRounds(); BuildIncidents();
        if (vm.IsAdmin) { BuildUsers(); BuildRoutes(); BuildAudit(); }
        foreach (var grid in new[] { users, routes, rounds, points, incidents }) grid.SelectionChanged += (_, _) => UpdateActions();
        UpdateActions();
        Loaded += async (_, _) => await Run(Refresh);
    }
    private void BuildRounds()
    {
        var panel = NewTab(vm.IsAdmin ? "Rondines" : "Mis rondines", vm.IsAdmin ? "Asigna recorridos y consulta su avance." : "Selecciona un rondín para iniciarlo y revisar sus puntos.");
        if (vm.IsAdmin) panel.Children.Add(ActionButton("Asignar rondín", Assign));
        AddSearch(panel, rounds, "Buscar por ruta, prefecto o estado");
        rounds.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Rounds))); rounds.Height = vm.IsAdmin ? 350 : 200; AddTable(panel, rounds, vm.IsAdmin ? "Todavía no hay rondines. Crea una ruta y un prefecto para asignar el primero." : "Aún no tienes rondines asignados. Consulta con tu administrador.");
        if (!vm.IsAdmin)
        {
            var bar = new WrapPanel(); bar.Children.Add(ActionButton("Iniciar rondín", Start, () => rounds.SelectedItem is RoundRow { Estado: RondinEstado.Programado })); bar.Children.Add(ActionButton("Completar punto", () => Mark(RegistroResultado.Completado), CanMark)); bar.Children.Add(ActionButton("Omitir punto", () => Mark(RegistroResultado.Omitido), CanMark)); bar.Children.Add(ActionButton("Cerrar rondín", CloseRound, () => rounds.SelectedItem is RoundRow { Estado: RondinEstado.EnCurso } && points.Items.Count > 0 && points.Items.Cast<PointRow>().All(x => x.Resultado != RegistroResultado.Pendiente))); panel.Children.Add(bar);
            panel.Children.Add(new TextBlock { Text = "Puntos del rondín seleccionado", FontWeight = FontWeights.Bold });
            points.Height = 200; AddTable(panel, points, "Los puntos aparecerán al iniciar el rondín seleccionado.");
            rounds.SelectionChanged += async (_, _) => await Run(LoadPoints);
        }
    }
    private void BuildIncidents()
    {
        var panel = NewTab("Incidencias", vm.IsAdmin ? "Revisa los reportes y da seguimiento hasta su cierre." : "Reporta hallazgos del recorrido en curso y adjunta evidencias.");
        var bar = new WrapPanel();
        if (vm.IsAdmin) bar.Children.Add(ActionButton("Cambiar estado", ChangeIncident, () => incidents.SelectedItem is IncidentRow { Estado: not IncidenciaEstado.Cerrada }));
        else { bar.Children.Add(ActionButton("Reportar incidencia", Report, () => rounds.SelectedItem is RoundRow { Estado: RondinEstado.EnCurso })); bar.Children.Add(ActionButton("Adjuntar evidencia", Attach, () => incidents.SelectedItem is IncidentRow { Archivos: < 5 } i && vm.Rounds.Any(r => r.Id == i.RondinId && r.Estado == RondinEstado.EnCurso))); }
        bar.Children.Add(ActionButton("Exportar evidencia", ShowEvidence, () => incidents.SelectedItem is IncidentRow { Archivos: > 0 })); panel.Children.Add(bar);
        if (!vm.IsAdmin) { var context = new TextBlock { Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray }; context.SetBinding(TextBlock.TextProperty, new Binding("SelectedItem.Ruta") { Source = rounds, StringFormat = "Rondín seleccionado: {0}", TargetNullValue = "Primero selecciona un recorrido en Mis rondines." }); panel.Children.Add(context); }
        AddSearch(panel, incidents, "Buscar por ruta, tipo, descripción o estado");
        incidents.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Incidents))); incidents.Height = 350; AddTable(panel, incidents, "No hay incidencias para mostrar. Los reportes aparecerán aquí.");
    }
    private void BuildUsers()
    {
        var panel = NewTab("Usuarios", "Crea cuentas para el equipo y administra su acceso."); var bar = new WrapPanel(); bar.Children.Add(ActionButton("Nuevo usuario", AddUser)); bar.Children.Add(ActionButton("Activar / desactivar", ToggleUser, () => users.SelectedItem is UserRow u && u.Id != store.Session.UserId)); panel.Children.Add(bar);
        AddSearch(panel, users, "Buscar por nombre, correo o rol");
        users.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Users))); users.Height = 350; AddTable(panel, users, "No hay usuarios que coincidan con la búsqueda.");
    }
    private void BuildRoutes()
    {
        var panel = NewTab("Rutas y puntos", "Organiza los recorridos y el orden de sus puntos de revisión."); var bar = new WrapPanel(); bar.Children.Add(ActionButton("Nueva ruta", AddRoute)); bar.Children.Add(ActionButton("Activar / desactivar", ToggleRoute, () => routes.SelectedItem is RouteRow)); bar.Children.Add(ActionButton("Ver puntos", ShowRoute, () => routes.SelectedItem is RouteRow)); panel.Children.Add(bar);
        AddSearch(panel, routes, "Buscar ruta por nombre");
        routes.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Routes))); routes.Height = 350; AddTable(panel, routes, "Crea tu primera ruta con sus puntos de revisión.");
    }
    private void BuildAudit()
    {
        var panel = NewTab("Bitácora", "Últimos 200 movimientos del sistema. Fechas expresadas en UTC.");
        var table = Table(("Fecha", "Fecha"), ("Acción", "Accion"), ("Entidad", "Entidad"), ("ID", "EntidadId"), ("Detalle", "Detalle")); table.Height = 420; table.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Audit))); panel.Children.Add(table);
    }
    private async Task Refresh()
    {
        var id = (rounds.SelectedItem as RoundRow)?.Id;
        var userId = (users.SelectedItem as UserRow)?.Id;
        var routeId = (routes.SelectedItem as RouteRow)?.Id;
        var incidentId = (incidents.SelectedItem as IncidentRow)?.Id;
        await vm.RefreshAsync();
        foreach (var (grid, search) in searches) Filter(grid, search.Text);
        if (id.HasValue) rounds.SelectedItem = vm.Rounds.FirstOrDefault(x => x.Id == id);
        users.SelectedItem = vm.Users.FirstOrDefault(x => x.Id == userId);
        routes.SelectedItem = vm.Routes.FirstOrDefault(x => x.Id == routeId);
        incidents.SelectedItem = vm.Incidents.FirstOrDefault(x => x.Id == incidentId);
        if (!vm.IsAdmin) await LoadPoints();
        status.Text = "Actualizado a las " + DateTime.Now.ToString("HH:mm");
        UpdateActions();
    }
    private async Task LoadPoints()
    {
        if (rounds.SelectedItem is not RoundRow r) { points.ItemsSource = null; return; }
        await using var db = store.Open();
        points.ItemsSource = await db.RegistrosPunto.AsNoTracking().Where(x => x.RondinId == r.Id).OrderBy(x => x.Orden).Select(x => new PointRow(x.Id, x.PuntoControlId, x.PuntoNombre, x.Resultado, x.Observaciones)).ToListAsync();
        UpdateActions();
    }
    private async Task AddUser()
    {
        var d = new Editor(this, "Nuevo usuario"); var name = d.Text("Nombre completo"); var email = d.Text("Correo"); var pass = d.Password("Contraseña (mínimo 12 caracteres)"); var role = d.Choice("Rol", new[] { Roles.Prefecto, Roles.Administrador });
        if (d.Save(() => store.AddUserAsync(name.Text, email.Text, pass.Password, (string)role.SelectedItem))) await Refresh();
    }
    private async Task AddRoute()
    {
        var d = new Editor(this, "Nueva ruta"); var name = d.Text("Nombre"); var description = d.Text("Descripción"); var pointNames = d.Text("Puntos en orden (uno por línea)", multiline: true);
        if (d.Save(() => store.AddRouteAsync(name.Text, description.Text, pointNames.Text))) await Refresh();
    }
    private async Task ToggleUser() { var u = Selected<UserRow>(users); if (!Confirm($"¿Quieres {(u.Activo ? "desactivar" : "activar")} la cuenta de {u.Nombre}?")) return; await store.ToggleUserAsync(u.Id); await Refresh(); }
    private async Task ToggleRoute() { var r = Selected<RouteRow>(routes); if (!Confirm($"¿Quieres {(r.Activa ? "desactivar" : "activar")} la ruta {r.Nombre}?")) return; await store.ToggleRouteAsync(r.Id); await Refresh(); }
    private async Task ShowRoute()
    {
        var r = Selected<RouteRow>(routes); await using var db = store.Open(); var list = await db.PuntosControl.Where(x => x.RutaId == r.Id).OrderBy(x => x.Orden).ToListAsync();
        MessageBox.Show(this, string.Join("\n", list.Select(x => $"{x.Orden}. {x.Nombre}")), r.Nombre);
    }
    private async Task Assign()
    {
        var available = vm.Routes.Where(x => x.Activa).ToList(); var prefects = vm.Users.Where(x => x.Activo && x.Rol == Roles.Prefecto).ToList();
        if (available.Count == 0 || prefects.Count == 0) throw new ReglaNegocioException("Primero crea una ruta y un usuario prefecto en los catálogos.");
        var d = new Editor(this, "Asignar rondín"); var route = d.Choice("Ruta", available, "Nombre"); var prefect = d.Choice("Prefecto", prefects, "Nombre"); var (date, time) = d.Schedule(); var notes = d.Text("Observaciones (opcional)");
        if (d.Save(async () =>
        {
            if (date.SelectedDate is not DateTime selected || !TimeOnly.TryParseExact(time.Text.Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var hour)) throw new ReglaNegocioException("Selecciona una fecha y escribe la hora en formato de 24 horas, por ejemplo 14:30.");
            var scheduled = DateTime.SpecifyKind(selected.Date + hour.ToTimeSpan(), DateTimeKind.Local).ToUniversalTime();
            await using var db = store.Open(); await new RondinService(db, store.Session).CrearAsync(new(((RouteRow)route.SelectedItem).Id, ((UserRow)prefect.SelectedItem).Id, scheduled, notes.Text), store.Session.UserId!);
        })) await Refresh();
    }
    private async Task Start() { var r = Selected<RoundRow>(rounds); await using var db = store.Open(); await new RondinService(db, store.Session).IniciarAsync(r.Id, store.Session.UserId!); await Refresh(); await LoadPoints(); }
    private async Task Mark(RegistroResultado result)
    {
        var r = Selected<RoundRow>(rounds); var p = Selected<PointRow>(points);
        var d = new Editor(this, result == RegistroResultado.Omitido ? "Omitir punto" : "Completar punto"); d.Label(p.Nombre); var notes = d.Text("Observación" + (result == RegistroResultado.Omitido ? " (obligatoria)" : " (opcional)"));
        if (d.Save(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).MarcarPuntoAsync(r.Id, p.Id, new(result, notes.Text), store.Session.UserId!); })) await Refresh();
    }
    private async Task CloseRound()
    {
        var r = Selected<RoundRow>(rounds); var d = new Editor(this, "Cerrar rondín"); d.Label("Al cerrar este recorrido ya no podrás cambiar sus puntos. Revisa tus registros antes de continuar."); var notes = d.Text("Observaciones finales (opcional)");
        if (d.Save(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).CerrarAsync(r.Id, notes.Text, store.Session.UserId!); })) await Refresh();
    }
    private async Task Report()
    {
        var r = Selected<RoundRow>(rounds); var p = points.SelectedItem as PointRow;
        var d = new Editor(this, "Reportar incidencia"); d.Label(p is null ? "Incidencia general del rondín: " + r.Ruta : "Punto: " + p.Nombre); var type = d.Text("Tipo (por ejemplo: mantenimiento)"); var priority = d.Choice("Prioridad", Enum.GetValues<IncidenciaPrioridad>()); var desc = d.Text("Describe lo que encontraste", multiline: true);
        if (d.Save(async () => { await using var db = store.Open(); await new IncidenciaService(db, store.Session).CrearAsync(new(r.Id, p?.PuntoId, type.Text, (IncidenciaPrioridad)priority.SelectedItem, desc.Text), store.Session.UserId!); })) await Refresh();
    }
    private async Task ChangeIncident()
    {
        var i = Selected<IncidentRow>(incidents);
        var allowed = i.Estado switch { IncidenciaEstado.Abierta => new[] { IncidenciaEstado.EnRevision, IncidenciaEstado.Resuelta }, IncidenciaEstado.EnRevision => new[] { IncidenciaEstado.Resuelta }, IncidenciaEstado.Resuelta => new[] { IncidenciaEstado.Cerrada }, _ => Array.Empty<IncidenciaEstado>() };
        if (allowed.Length == 0) return;
        var d = new Editor(this, "Seguimiento de incidencia"); d.Label("Estado actual: " + FriendlyValueConverter.Label(i.Estado)); var state = d.Choice("Nuevo estado", allowed); var comment = d.Text("Comentario (opcional)");
        if (d.Save(async () => { await using var db = store.Open(); await new IncidenciaService(db, store.Session).CambiarEstadoAsync(i.Id, new((IncidenciaEstado)state.SelectedItem, comment.Text), store.Session.UserId!); })) await Refresh();
    }
    private async Task Attach()
    {
        var i = Selected<IncidentRow>(incidents); var file = new OpenFileDialog { Filter = "Evidencias|*.jpg;*.jpeg;*.png;*.pdf" }; if (file.ShowDialog(this) != true) return;
        await store.SaveEvidenceAsync(i.Id, file.FileName); await Refresh();
    }
    private async Task ShowEvidence()
    {
        var i = Selected<IncidentRow>(incidents); await using var db = store.Open(); var files = await db.Evidencias.Where(x => x.IncidenciaId == i.Id).ToListAsync();
        if (files.Count == 0) throw new ReglaNegocioException("Esta incidencia no tiene archivos.");
        var d = new Editor(this, "Exportar evidencia"); var choice = d.Choice("Archivo", files, "NombreOriginal"); if (!d.Accept()) return;
        var selected = (Evidencia)choice.SelectedItem; var target = new SaveFileDialog { FileName = selected.NombreOriginal, Filter = "Archivo|*.*" };
        if (target.ShowDialog(this) == true) { System.IO.File.Copy(selected.RutaArchivo, target.FileName, true); status.Text = "Evidencia exportada."; }
    }
    private Task Logout()
    {
        if (!Confirm("¿Quieres cerrar tu sesión? Los registros guardados se conservarán.")) return Task.CompletedTask;
        store.Session.SignOut(); Hide(); var access = new AccessWindow(store, false) { Owner = this };
        if (access.ShowDialog() == true) { var next = new MainWindow(store); System.Windows.Application.Current.MainWindow = next; next.Show(); }
        Close(); return Task.CompletedTask;
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true; tabs.IsEnabled = false; UpdateActions();
        status.Text = "Procesando…";
        try { await action(); }
        catch (DbUpdateConcurrencyException) { MessageBox.Show(this, "Los datos cambiaron en otra ventana. Actualiza e intenta de nuevo.", "Ftracker"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ftracker", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { busy = false; tabs.IsEnabled = true; UpdateActions(); if (status.Text == "Procesando…") status.Text = "Listo."; }
    }
    private bool CanMark() => rounds.SelectedItem is RoundRow { Estado: RondinEstado.EnCurso } && points.SelectedItem is PointRow { Resultado: RegistroResultado.Pendiente };
    private bool Confirm(string message) => MessageBox.Show(this, message, "Confirmar · Ftracker", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void UpdateActions() { foreach (var (button, enabled) in contextualActions) button.IsEnabled = !busy && enabled(); }
    private Button ActionButton(string title, Func<Task> action, Func<bool>? enabledWhen = null)
    {
        var b = new Button { Content = title, ToolTip = enabledWhen is null ? title : "Selecciona un registro compatible para usar esta acción." };
        ToolTipService.SetShowOnDisabled(b, true);
        System.Windows.Automation.AutomationProperties.SetName(b, title);
        contextualActions.Add((b, enabledWhen ?? (() => true)));
        b.Click += async (_, _) => await Run(action); return b;
    }
    private StackPanel NewTab(string title, string description = "")
    {
        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold });
        if (description.Length > 0) p.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 6, 0, 18) });
        tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return p;
    }
    private void AddSearch(StackPanel panel, DataGrid grid, string hint)
    {
        var bar = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var clear = new Button { Content = "Limpiar", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(8, 5, 0, 14) }; DockPanel.SetDock(clear, Dock.Right); bar.Children.Add(clear);
        var search = new TextBox { ToolTip = hint, HorizontalAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(search, hint);
        search.TextChanged += (_, _) => { Filter(grid, search.Text); UpdateActions(); };
        clear.Click += (_, _) => { search.Clear(); search.Focus(); };
        bar.Children.Add(search); panel.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.SlateGray, FontSize = 12 }); panel.Children.Add(bar); searches.Add((grid, search));
    }
    private static void Filter(DataGrid grid, string text)
    {
        if (grid.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        var query = text.Trim();
        view.Filter = query.Length == 0 ? null : row => System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(SearchText(row), query, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
    }
    private static string SearchText(object row) => row switch
    {
        UserRow u => $"{u.Nombre} {u.Correo} {u.Rol} {FriendlyValueConverter.Label(u.Activo)}",
        RouteRow r => $"{r.Nombre} {FriendlyValueConverter.Label(r.Activa)}",
        RoundRow r => $"{r.Ruta} {r.Prefecto} {FriendlyValueConverter.Label(r.Estado)} {FriendlyValueConverter.Label(r.Programado)}",
        IncidentRow i => $"{i.Ruta} {i.Tipo} {i.Descripcion} {FriendlyValueConverter.Label(i.Prioridad)} {FriendlyValueConverter.Label(i.Estado)}",
        _ => row.ToString() ?? ""
    };
    private static void AddTable(StackPanel panel, DataGrid table, string message)
    {
        var container = new Grid(); container.Children.Add(table);
        var placeholder = new TextBlock { Text = message + "\nSi estás buscando, prueba con otro texto o pulsa Limpiar.", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 440, Margin = new Thickness(24), Foreground = Brushes.SlateGray, IsHitTestVisible = false };
        placeholder.SetBinding(VisibilityProperty, new Binding("Items.Count") { Source = table, Converter = new EmptyVisibilityConverter() });
        container.Children.Add(placeholder); panel.Children.Add(container);
    }
    private static T Selected<T>(DataGrid grid) where T : class => grid.SelectedItem as T ?? throw new ReglaNegocioException("Selecciona un registro en la tabla.");
    private static DataGrid Table(params (string Header, string Path)[] columns)
    {
        var table = new DataGrid(); foreach (var c in columns) table.Columns.Add(new DataGridTextColumn { Header = c.Header, Binding = new Binding(c.Path) { Converter = new FriendlyValueConverter() }, MinWidth = c.Path == "Descripcion" ? 180 : 90, Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); return table;
    }
}

internal sealed class Editor : Window
{
    private readonly StackPanel panel = new() { Margin = new Thickness(22) };
    private readonly List<Control> fields = [];
    private bool saving;
    public Editor(Window owner, string title)
    {
        Owner = owner; Title = title; Style = (Style)FindResource(typeof(Window)); Width = 560; SizeToContent = SizeToContent.Height; MaxHeight = Math.Min(780, SystemParameters.WorkArea.Height - 30); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer { Content = new Border { Background = Brushes.White, Margin = new Thickness(12), CornerRadius = new CornerRadius(10), Child = panel }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 20) });
        Loaded += (_, _) => fields.FirstOrDefault()?.Focus();
        Closing += (_, e) => { if (saving) e.Cancel = true; };
    }
    public void Label(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
    public TextBox Text(string label, string initial = "", bool multiline = false) { Label(label); var box = new TextBox { Text = initial, AcceptsReturn = multiline, Height = multiline ? 110 : double.NaN, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled }; AddField(box, label); return box; }
    public PasswordBox Password(string label) { Label(label); var box = new PasswordBox(); AddField(box, label); return box; }
    public ComboBox Choice<T>(string label, IEnumerable<T> items, string display = "")
    {
        Label(label); var box = new ComboBox { ItemsSource = items, SelectedIndex = 0 };
        if (display.Length > 0) box.DisplayMemberPath = display;
        else { var factory = new FrameworkElementFactory(typeof(TextBlock)); factory.SetBinding(TextBlock.TextProperty, new Binding { Converter = new FriendlyValueConverter() }); box.ItemTemplate = new DataTemplate { VisualTree = factory }; }
        AddField(box, label); return box;
    }
    public (DatePicker Date, TextBox Time) Schedule()
    {
        Label("Fecha del recorrido"); var date = new DatePicker { SelectedDate = DateTime.Today }; AddField(date, "Fecha del recorrido");
        var time = Text("Hora local (24 horas, por ejemplo 14:30)", DateTime.Now.ToString("HH:mm")); return (date, time);
    }
    private void AddField(Control field, string name) { panel.Children.Add(field); fields.Add(field); System.Windows.Automation.AutomationProperties.SetName(field, name); }
    public bool Accept() => Save(() => Task.CompletedTask, "Continuar");
    public bool Save(Func<Task> save, string label = "Guardar")
    {
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) }; panel.Children.Add(error);
        var buttons = new WrapPanel(); var ok = new Button { Content = label }; var cancel = new Button { Content = "Cancelar", IsCancel = true, Style = (Style)FindResource("SecondaryButton") };
        ok.Click += async (_, _) =>
        {
            saving = true; ok.IsEnabled = false; cancel.IsEnabled = false; error.Text = ""; ok.Content = "Guardando…";
            foreach (var field in fields) field.IsEnabled = false;
            try { await save(); saving = false; DialogResult = true; }
            catch (DbUpdateConcurrencyException) { error.Text = "El registro cambió. Cancela, actualiza los datos y vuelve a intentarlo."; }
            catch (ReglaNegocioException ex) { error.Text = ex.Message; }
            catch (Exception) { error.Text = "No se pudo guardar. Revisa los datos e inténtalo nuevamente."; }
            finally { saving = false; ok.Content = label; ok.IsEnabled = true; cancel.IsEnabled = true; foreach (var field in fields) field.IsEnabled = true; }
        };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons); return ShowDialog() == true;
    }
}

internal sealed class EmptyVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

internal sealed class FriendlyValueConverter : IValueConverter
{
    public static string Label(object value) => value switch { RondinEstado.EnCurso => "En curso", IncidenciaEstado.EnRevision => "En revisión", IncidenciaPrioridad.Critica => "Crítica", bool active => active ? "Activo" : "Inactivo", DateTime date => date.ToString("dd/MM/yyyy HH:mm"), _ => value?.ToString() ?? "" };
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Label(value);
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
