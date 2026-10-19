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
public sealed partial class MainWindow : Window
{
    private readonly LocalStore store;
    private readonly MainViewModel vm;
    private readonly TabControl tabs = new();
    private readonly DataGrid users = Table(("Nombre", "Nombre"), ("Correo", "Correo"), ("Rol", "Rol"), ("Activo", "Activo"));
    private readonly DataGrid routes = Table(("Ruta", "Nombre"), ("Puntos", "Puntos"), ("Activa", "Activa"));
    private readonly DataGrid rounds = Table(("Ruta", "Ruta"), ("Prefecto", "Prefecto"), ("Programado", "Programado"), ("Estado", "Estado"), ("Cerrado el", "CerradoEl"), ("Alerta", "Alerta"));
    private readonly DataGrid points = Table(("Punto", "Nombre"), ("Resultado", "Resultado"), ("Observación", "Observaciones"), ("Código", "Verificacion"));
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
        Background = ThemeManager.Get("Page");
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(ThemeManager.ToggleButton()); actions.Children.Add(PasswordButton());
        var update = ActionButton("Actualizar datos", Refresh); update.Style = (Style)FindResource("SecondaryButton"); actions.Children.Add(update);
        var logout = ActionButton("Cerrar sesión", Logout); logout.Style = (Style)FindResource("SecondaryButton"); actions.Children.Add(logout);
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var identity = new StackPanel(); identity.Children.Add(Brand.Header(26));
        identity.Children.Add(new TextBlock { Text = $"{store.Session.User!.NombreCompleto} · {store.Session.Role}", FontSize = 12, Foreground = ThemeManager.Get("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        header.Children.Add(identity);
        status.Foreground = ThemeManager.Get("Muted"); status.FontSize = 12;
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); AddAlertBanner(root); root.Children.Add(tabs);
        if (vm.IsAdmin) { BuildUsers(); BuildRoutes(); }
        BuildRounds(); BuildIncidents();
        if (vm.IsAdmin) { BuildReports(); BuildAudit(); }
        foreach (var grid in new[] { users, routes, rounds, points, incidents }) grid.SelectionChanged += (_, _) => UpdateActions();
        UpdateActions();
        Loaded += async (_, _) => await Run(Refresh);
    }
    private void BuildRounds()
    {
        rounds.Columns[4].SortMemberPath = nameof(RoundRow.CerradoEn);
        rounds.Columns[4].MinWidth = 145;
        var panel = NewTab(vm.IsAdmin ? "Rondines" : "Mis rondines", vm.IsAdmin ? "Asigna recorridos y consulta su avance." : "Selecciona un rondín para iniciarlo y revisar sus puntos.");
        var management = new WrapPanel(); panel.Children.Add(management);
        if (vm.IsAdmin)
        {
            management.Children.Add(ActionButton("Asignar rondín", Assign));
            management.Children.Add(ActionButton("Reasignar", ReassignRound, () => rounds.SelectedItem is RoundRow { Estado: RondinEstado.Programado }));
            management.Children.Add(ActionButton("Cancelar rondín", CancelRound, () => rounds.SelectedItem is RoundRow { Estado: RondinEstado.Programado or RondinEstado.EnCurso }));
            management.Children.Add(DangerButton("Eliminar rondín", DeleteRound, () => rounds.SelectedItem is RoundRow));
        }
        management.Children.Add(ActionButton("Ver croquis", ShowRoundCroquis, () => rounds.SelectedItem is RoundRow));
        AddSearch(panel, rounds, "Buscar por ruta, prefecto o estado");
        rounds.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Rounds))); rounds.Height = vm.IsAdmin ? 350 : 200; AddTable(panel, rounds, vm.IsAdmin ? "Todavía no hay rondines. Crea una ruta y un prefecto para asignar el primero." : "Aún no tienes rondines asignados. Consulta con tu administrador.");
        if (!vm.IsAdmin)
        {
            ConfigurePointEvidence();
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
        bar.Children.Add(ActionButton("Ver evidencia", PreviewEvidence, () => incidents.SelectedItem is IncidentRow { Archivos: > 0 }));
        bar.Children.Add(ActionButton("Exportar evidencia", ShowEvidence, () => incidents.SelectedItem is IncidentRow { Archivos: > 0 })); panel.Children.Add(bar);
        if (!vm.IsAdmin) { var context = new TextBlock { Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Get("Muted") }; context.SetBinding(TextBlock.TextProperty, new Binding("SelectedItem.Ruta") { Source = rounds, StringFormat = "Rondín seleccionado: {0}", TargetNullValue = "Primero selecciona un recorrido en Mis rondines." }); panel.Children.Add(context); }
        AddSearch(panel, incidents, "Buscar por ruta, tipo, descripción o estado");
        incidents.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Incidents))); incidents.Height = 350; AddTable(panel, incidents, "No hay incidencias para mostrar. Los reportes aparecerán aquí.");
    }
    private void BuildUsers()
    {
        var panel = NewTab("Usuarios", "Crea cuentas para el equipo y administra su acceso."); var bar = new WrapPanel(); bar.Children.Add(ActionButton("Nuevo usuario", AddUser)); bar.Children.Add(ActionButton("Editar usuario", EditUser, () => users.SelectedItem is UserRow)); bar.Children.Add(ActionButton("Restablecer contraseña", ResetUserPassword, () => users.SelectedItem is UserRow u && u.Id != store.Session.UserId)); bar.Children.Add(ActionButton("Activar / desactivar", ToggleUser, () => users.SelectedItem is UserRow u && u.Id != store.Session.UserId)); bar.Children.Add(DangerButton("Eliminar usuario", DeleteUser, () => users.SelectedItem is UserRow u && u.Id != store.Session.UserId)); panel.Children.Add(bar);
        AddSearch(panel, users, "Buscar por nombre, correo o rol");
        users.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Users))); users.Height = 350; AddTable(panel, users, "No hay usuarios que coincidan con la búsqueda.");
    }
    private void BuildRoutes()
    {
        var panel = NewTab("Rutas y puntos", "Organiza los recorridos y el orden de sus puntos de revisión."); var bar = new WrapPanel(); bar.Children.Add(ActionButton("Nueva ruta", AddRoute)); bar.Children.Add(ActionButton("Activar / desactivar", ToggleRoute, () => routes.SelectedItem is RouteRow)); bar.Children.Add(ActionButton("Ver puntos", ShowRoute, () => routes.SelectedItem is RouteRow)); bar.Children.Add(ActionButton("Editar puntos", EditRoutePoints, () => routes.SelectedItem is RouteRow)); panel.Children.Add(bar);
        bar.Children.Add(ActionButton("Ubicar en croquis", EditRouteCroquis, () => routes.SelectedItem is RouteRow));
        bar.Children.Add(DangerButton("Eliminar ruta", DeleteRoute, () => routes.SelectedItem is RouteRow));
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
        status.Text = "Actualizado a las " + DateTime.Now.ToString("HH:mm"); UpdateAlertBanner();
        UpdateActions();
    }
    private async Task LoadPoints()
    {
        if (rounds.SelectedItem is not RoundRow r) { points.ItemsSource = null; return; }
        await using var db = store.Open();
        var linked = await db.Incidencias.AsNoTracking()
            .Where(x => x.RondinId == r.Id && x.PuntoControlId != null && x.Evidencias.Any())
            .OrderByDescending(x => x.CreadoEn)
            .Select(x => new { x.Id, x.PuntoControlId, x.Tipo, x.Descripcion }).ToListAsync();
        var rows = await db.RegistrosPunto.AsNoTracking().Where(x => x.RondinId == r.Id).OrderBy(x => x.Orden).Select(x => new PointRow(x.Id, x.PuntoControlId, x.PuntoNombre, x.Resultado, x.Observaciones, x.Verificado)).ToListAsync();
        points.ItemsSource = rows.Select(p => p with
        {
            IncidenciasConEvidencia = linked.Where(i => i.PuntoControlId == p.PuntoId)
                .Select(i => new PointIncident(i.Id, i.Tipo + ": " + i.Descripcion)).ToList()
        }).ToList();
        UpdateActions();
    }
    private void ConfigurePointEvidence()
    {
        var cell = new FrameworkElementFactory(typeof(DockPanel));
        cell.SetValue(DockPanel.LastChildFillProperty, true);
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(ContentControl.ContentProperty, "Evidencia");
        button.SetValue(FrameworkElement.StyleProperty, FindResource("EvidenceLink"));
        button.SetValue(Control.FontSizeProperty, 12.0);
        button.SetValue(Control.PaddingProperty, new Thickness(8, 3, 8, 3));
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 2, 4, 2));
        button.SetValue(DockPanel.DockProperty, Dock.Right);
        button.SetValue(FrameworkElement.ToolTipProperty, "Ver evidencias de las incidencias de este punto.");
        button.SetBinding(VisibilityProperty, new Binding(nameof(PointRow.TieneEvidencia)) { Converter = new BooleanToVisibilityConverter() });
        button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Ver evidencias del punto");
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler(async (sender, e) =>
        {
            e.Handled = true;
            if (((Button)sender).DataContext is PointRow point) await Run(() => PreviewPointEvidence(point));
        }));
        cell.AppendChild(button);
        var notes = new FrameworkElementFactory(typeof(TextBlock));
        notes.SetBinding(TextBlock.TextProperty, new Binding(nameof(PointRow.Observaciones)));
        notes.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        notes.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        cell.AppendChild(notes);
        points.Columns[2] = new DataGridTemplateColumn
        {
            Header = "Observación", MinWidth = 220, Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            CellTemplate = new DataTemplate { VisualTree = cell }
        };
    }
    private async Task PreviewPointEvidence(PointRow point)
    {
        var linked = point.IncidenciasConEvidencia;
        if (linked.Count == 0) throw new ReglaNegocioException("Este punto no tiene incidencias con evidencias adjuntas.");
        var selected = linked[0];
        if (linked.Count > 1)
        {
            var dialog = new Editor(this, "Evidencias del punto"); dialog.Label(point.Nombre);
            var choice = dialog.Choice("Selecciona la incidencia", linked, nameof(PointIncident.Detalle));
            if (!dialog.Accept()) return;
            selected = (PointIncident)choice.SelectedItem;
        }
        var window = await EvidenceWindow.CreateAsync(store, selected.Id);
        window.Owner = this; window.ShowDialog();
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
    private async Task DeleteUser()
    {
        var u = Selected<UserRow>(users);
        if (!Confirm($"¿Eliminar definitivamente la cuenta de {u.Nombre} ({u.Correo})?\n\nEsta acción no se puede deshacer. Si solo quieres impedir su acceso, usa Activar / desactivar.", destructive: true)) return;
        await store.DeleteUserAsync(u.Id); await Refresh(); status.Text = $"Usuario {u.Nombre} eliminado.";
    }
    private async Task DeleteRoute()
    {
        var r = Selected<RouteRow>(routes);
        if (!Confirm($"¿Eliminar definitivamente la ruta {r.Nombre} con sus {r.Puntos} punto(s) y sus ubicaciones en el croquis?\n\nEsta acción no se puede deshacer. Si la ruta ya tiene rondines, deberás eliminarlos primero; para conservarlos, desactiva la ruta.", destructive: true)) return;
        await store.DeleteRouteAsync(r.Id); await Refresh(); status.Text = $"Ruta {r.Nombre} eliminada.";
    }
    private async Task DeleteRound()
    {
        var r = Selected<RoundRow>(rounds);
        var warning = r.Estado == RondinEstado.EnCurso ? "\n\nATENCIÓN: el rondín está en curso; el prefecto perderá su avance." : "";
        if (!Confirm($"¿Eliminar definitivamente el rondín de {r.Ruta} asignado a {r.Prefecto} ({FriendlyValueConverter.Label(r.Programado)})?\n\nSe borrarán también sus puntos registrados, incidencias y archivos de evidencia. Esta acción no se puede deshacer. Si solo quieres detenerlo conservando el historial, usa Cancelar rondín.{warning}", destructive: true)) return;
        await store.DeleteRoundAsync(r.Id); await Refresh(); status.Text = "Rondín eliminado.";
    }
    private async Task ShowRoute()
    {
        var r = Selected<RouteRow>(routes); await using var db = store.Open(); var list = await db.PuntosControl.Where(x => x.RutaId == r.Id).OrderBy(x => x.Orden).ToListAsync();
        MessageBox.Show(this, string.Join("\n", list.Select(x => $"{x.Orden}. {x.Nombre}")), r.Nombre);
    }
    private async Task EditRouteCroquis()
    {
        var route = Selected<RouteRow>(routes);
        var window = await CroquisWindow.ForRouteAsync(store, route.Id, route.Nombre);
        window.Owner = this; window.ShowDialog(); await Refresh();
    }
    private async Task ShowRoundCroquis()
    {
        var round = Selected<RoundRow>(rounds);
        var window = await CroquisWindow.ForRoundAsync(store, round.Id);
        window.Owner = this; window.ShowDialog(); await Refresh();
    }
    private async Task Assign()
    {
        var available = vm.Routes.Where(x => x.Activa).ToList(); var prefects = vm.Users.Where(x => x.Activo && x.Rol == Roles.Prefecto).ToList();
        if (available.Count == 0 || prefects.Count == 0) throw new ReglaNegocioException("Primero crea una ruta y un usuario prefecto en los catálogos.");
        var d = new Editor(this, "Asignar rondín"); var route = d.Choice("Ruta", available, "Nombre"); var prefect = d.Choice("Prefecto", prefects, "Nombre"); var (date, time) = d.Schedule(); var notes = d.Text("Observaciones (opcional)");
        if (d.Save(async () =>
        {
            if (date.SelectedDate is not DateTime selected) throw new ReglaNegocioException("Selecciona la fecha del recorrido.");
            var scheduled = DateTime.SpecifyKind(selected.Date + time.Value.ToTimeSpan(), DateTimeKind.Local).ToUniversalTime();
            await using var db = store.Open(); await new RondinService(db, store.Session).CrearAsync(new(((RouteRow)route.SelectedItem).Id, ((UserRow)prefect.SelectedItem).Id, scheduled, notes.Text), store.Session.UserId!);
        })) await Refresh();
    }
    private async Task Start() { var r = Selected<RoundRow>(rounds); await using var db = store.Open(); await new RondinService(db, store.Session).IniciarAsync(r.Id, store.Session.UserId!); await Refresh(); await LoadPoints(); }
    private async Task ReassignRound()
    {
        var round = Selected<RoundRow>(rounds);
        await using var db = store.Open();
        var current = await db.Rondines.AsNoTracking().SingleAsync(x => x.Id == round.Id);
        var available = vm.Users.Where(x => x.Activo && x.Rol == Roles.Prefecto && x.Id != current.PrefectoId).ToList();
        if (available.Count == 0) throw new ReglaNegocioException("Necesitas otro prefecto activo para reasignar este rondín.");
        var dialog = new Editor(this, "Reasignar rondín"); dialog.Label($"Ruta: {round.Ruta}. Prefecto actual: {round.Prefecto}.");
        var prefect = dialog.Choice("Nuevo prefecto", available, "Nombre"); var reason = dialog.Text("Motivo (obligatorio)", multiline: true); reason.MaxLength = 1000;
        if (dialog.Save(async () =>
        {
            await using var context = store.Open();
            await new RondinService(context, store.Session).ReasignarAsync(round.Id, new(((UserRow)prefect.SelectedItem).Id, reason.Text), store.Session.UserId!);
        })) await Refresh();
    }
    private async Task CancelRound()
    {
        var round = Selected<RoundRow>(rounds); var dialog = new Editor(this, "Cancelar rondín");
        dialog.Label($"Ruta: {round.Ruta}. Los registros e incidencias se conservarán, pero no se podrá continuar el recorrido. Esta acción no se puede deshacer.");
        var reason = dialog.Text("Motivo (obligatorio)", multiline: true); reason.MaxLength = 1000;
        if (dialog.Save(async () =>
        {
            await using var db = store.Open();
            await new RondinService(db, store.Session).CancelarAsync(round.Id, reason.Text, store.Session.UserId!);
        }, "Confirmar cancelación")) await Refresh();
    }
    private async Task Mark(RegistroResultado result)
    {
        var r = Selected<RoundRow>(rounds); var p = Selected<PointRow>(points);
        var d = new Editor(this, result == RegistroResultado.Omitido ? "Omitir punto" : "Completar punto"); d.Label(p.Nombre); var code = result == RegistroResultado.Completado && await store.RequiresCodeAsync(p.Id) ? d.Text("Código del punto (escanéalo con el lector o escríbelo)") : null; var notes = d.Text("Observación" + (result == RegistroResultado.Omitido ? " (obligatoria)" : " (opcional)"));
        if (d.Save(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).MarcarPuntoAsync(r.Id, p.Id, new(result, notes.Text, code?.Text), store.Session.UserId!); })) await Refresh();
    }
    private async Task CloseRound()
    {
        var r = Selected<RoundRow>(rounds); var d = new Editor(this, "Cerrar rondín"); d.Label("Al cerrar este recorrido ya no podrás cambiar sus puntos. Revisa tus registros antes de continuar."); var notes = d.Text("Observaciones finales (opcional)");
        if (d.Save(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).CerrarAsync(r.Id, notes.Text, store.Session.UserId!); })) await Refresh();
    }
    private async Task Report()
    {
        var r = Selected<RoundRow>(rounds); var p = points.SelectedItem as PointRow;
        if (IncidentEditor.Report(this, store, r.Id, p?.PuntoId, p is null ? "Incidencia general del rondín: " + r.Ruta : "Punto: " + p.Nombre)) await Refresh();
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
        var i = Selected<IncidentRow>(incidents); var files = await store.GetEvidenceAsync(i.Id);
        if (files.Count == 0) throw new ReglaNegocioException("Esta incidencia no tiene archivos.");
        var d = new Editor(this, "Exportar evidencia"); var choice = d.Choice("Archivo", files, "NombreOriginal"); if (!d.Accept()) return;
        var selected = (Evidencia)choice.SelectedItem; var target = new SaveFileDialog { FileName = selected.NombreOriginal, Filter = "Archivo|*.*" };
        if (target.ShowDialog(this) == true) { var bytes = await store.ReadEvidenceAsync(selected.Id); await System.IO.File.WriteAllBytesAsync(target.FileName, bytes); status.Text = "Evidencia exportada."; }
    }
    private async Task PreviewEvidence()
    {
        var incident = Selected<IncidentRow>(incidents);
        var window = await EvidenceWindow.CreateAsync(store, incident.Id);
        window.Owner = this; window.ShowDialog();
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
    private bool Confirm(string message, bool destructive = false) => MessageBox.Show(this, message, destructive ? "Confirmar eliminación · Ftracker" : "Confirmar · Ftracker", MessageBoxButton.YesNo, destructive ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void UpdateActions() { foreach (var (button, enabled) in contextualActions) button.IsEnabled = !busy && enabled(); }
    private Button ActionButton(string title, Func<Task> action, Func<bool>? enabledWhen = null)
    {
        var b = new Button { Content = title, ToolTip = enabledWhen is null ? title : "Selecciona un registro compatible para usar esta acción." };
        ToolTipService.SetShowOnDisabled(b, true);
        System.Windows.Automation.AutomationProperties.SetName(b, title);
        contextualActions.Add((b, enabledWhen ?? (() => true)));
        b.Click += async (_, _) => await Run(action); return b;
    }
    private Button DangerButton(string title, Func<Task> action, Func<bool> enabledWhen)
    {
        var b = ActionButton(title, action, enabledWhen); b.Style = (Style)FindResource("DangerButton"); return b;
    }
    private StackPanel NewTab(string title, string description = "")
    {
        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold });
        if (description.Length > 0) p.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Get("Muted"), Margin = new Thickness(0, 6, 0, 18) });
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
        bar.Children.Add(search); panel.Children.Add(new TextBlock { Text = hint, Foreground = ThemeManager.Get("Muted"), FontSize = 12 }); panel.Children.Add(bar); searches.Add((grid, search));
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
        RoundRow r => $"{r.Ruta} {r.Prefecto} {FriendlyValueConverter.Label(r.Estado)} {FriendlyValueConverter.Label(r.Programado)} {r.CerradoEl} {r.Alerta}",
        IncidentRow i => $"{i.Ruta} {i.Tipo} {i.Descripcion} {FriendlyValueConverter.Label(i.Prioridad)} {FriendlyValueConverter.Label(i.Estado)}",
        _ => row.ToString() ?? ""
    };
    private static void AddTable(StackPanel panel, DataGrid table, string message)
    {
        var container = new Grid(); container.Children.Add(table);
        var placeholder = new TextBlock { Text = message + "\nSi estás buscando, prueba con otro texto o pulsa Limpiar.", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 440, Margin = new Thickness(24), Foreground = ThemeManager.Get("Muted"), IsHitTestVisible = false };
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
        Content = new ScrollViewer { Content = new Border { Background = ThemeManager.Get("Surface"), Margin = new Thickness(12), CornerRadius = new CornerRadius(10), Child = panel }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 20) });
        Loaded += (_, _) => fields.FirstOrDefault()?.Focus();
        Closing += (_, e) => { if (saving) e.Cancel = true; };
    }
    public void Label(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
    public TextBox Text(string label, string initial = "", bool multiline = false) { Label(label); var box = new TextBox { Text = initial, AcceptsReturn = multiline, Height = multiline ? 110 : double.NaN, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled }; AddField(box, label); return box; }
    public DatePicker Date(string label, DateTime? initial = null) { Label(label); var date = new DatePicker { SelectedDate = initial, Language = System.Windows.Markup.XmlLanguage.GetLanguage("es-MX"), SelectedDateFormat = DatePickerFormat.Short }; AddField(date, label); return date; }
    public PasswordBox Password(string label) { Label(label); var box = new PasswordBox(); AddField(box, label); return box; }
    public ComboBox Choice<T>(string label, IEnumerable<T> items, string display = "")
    {
        Label(label); var box = new ComboBox { ItemsSource = items, SelectedIndex = 0 };
        if (display.Length > 0) box.DisplayMemberPath = display;
        else { var factory = new FrameworkElementFactory(typeof(TextBlock)); factory.SetBinding(TextBlock.TextProperty, new Binding { Converter = new FriendlyValueConverter() }); box.ItemTemplate = new DataTemplate { VisualTree = factory }; }
        AddField(box, label); return box;
    }
    public (DatePicker Date, TimeSelector Time) Schedule()
    {
        Label("Fecha del recorrido"); var date = new DatePicker { SelectedDate = DateTime.Today, Language = System.Windows.Markup.XmlLanguage.GetLanguage("es-MX"), SelectedDateFormat = DatePickerFormat.Short }; AddField(date, "Fecha del recorrido");
        Label("Hora"); var time = new TimeSelector(TimeOnly.FromDateTime(DateTime.Now)) { SelectedDate = date.SelectedDate };
        date.SelectedDateChanged += (_, _) => time.SelectedDate = date.SelectedDate;
        AddField(time, "Hora"); return (date, time);
    }
    private void AddField(Control field, string name) { panel.Children.Add(field); fields.Add(field); System.Windows.Automation.AutomationProperties.SetName(field, name); }
    public bool Accept() => Save(() => Task.CompletedTask, "Continuar");
    public bool Save(Func<Task> save, string label = "Guardar")
    {
        var error = new TextBlock { Foreground = ThemeManager.Get("Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) }; panel.Children.Add(error);
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
