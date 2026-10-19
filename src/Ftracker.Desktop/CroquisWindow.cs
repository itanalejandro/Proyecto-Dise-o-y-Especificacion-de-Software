using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Desktop;

internal sealed class CroquisWindow : Window
{
    private readonly LocalStore store;
    private readonly Guid targetId;
    private readonly bool editing;
    private bool canRegister;
    private bool dirty;
    private bool busy;
    private List<CroquisPunto> points;
    private readonly ListBox list = new() { MinHeight = 140 };
    private readonly Canvas overlay = new() { Background = Brushes.Transparent };
    private readonly Grid map = new();
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Slider zoom = new() { Minimum = 0.25, Maximum = 2.5, Value = 0.7, Width = 135, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button save = new() { Content = "Guardar ubicaciones" };
    private readonly Button clear = new() { Content = "Quitar ubicación" };
    private readonly Button complete = new() { Content = "Completar punto" };
    private readonly Button omit = new() { Content = "Omitir punto" };
    private readonly Button report = new() { Content = "Reportar incidencia" };
    private CroquisPunto? Selected => (list.SelectedItem as PointItem)?.Point;
    private sealed record PointItem(CroquisPunto Point, string Label);

    public static async Task<CroquisWindow> ForRouteAsync(LocalStore store, Guid id, string name) =>
        new(store, id, name, true, false, await store.GetRouteCroquisAsync(id));

    public static async Task<CroquisWindow> ForRoundAsync(LocalStore store, Guid id)
    {
        var round = await store.GetRoundCroquisAsync(id);
        return new(store, id, $"{round.Nombre} · {FriendlyValueConverter.Label(round.Estado)}", false, round.PuedeRegistrar, round.Puntos);
    }

    private CroquisWindow(LocalStore store, Guid id, string name, bool editing, bool canRegister, IReadOnlyList<CroquisPunto> points)
    {
        this.store = store; targetId = id; this.editing = editing; this.canRegister = canRegister; this.points = points.ToList();
        Title = "Croquis · " + name; Icon = Brand.Icon; Style = (Style)FindResource(typeof(Window));
        Width = 1200; Height = 820; MinWidth = 900; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = name, FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = editing ? "Selecciona un punto en la lista y haz clic en el croquis para colocarlo o moverlo. Guarda al terminar."
                : "Selecciona un marcador o un punto de la lista para consultar su estado. Los puntos sin ubicación siguen disponibles en la lista.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8)
        });
        var tools = new WrapPanel(); heading.Children.Add(tools);
        tools.Children.Add(Tool("−", () => zoom.Value -= 0.15)); tools.Children.Add(zoom);
        tools.Children.Add(Tool("+", () => zoom.Value += 0.15)); tools.Children.Add(Tool("Ajustar", Fit));
        tools.Children.Add(new TextBlock { Text = "Azul: pendiente · Verde: completado · Ámbar: omitido", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        System.Windows.Automation.AutomationProperties.SetName(zoom, "Zoom del croquis");
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(status);
        footer.Children.Add(new TextBlock { Text = "Croquis orientativo, sin GPS. Las líneas indican el orden de los puntos, no caminos reales.", FontSize = 12, Foreground = ThemeManager.Get("Muted"), Margin = new Thickness(0, 6, 0, 0) });
        var sidebar = new DockPanel { Width = 290, Margin = new Thickness(16, 10, 0, 0) }; DockPanel.SetDock(sidebar, Dock.Right); root.Children.Add(sidebar);
        var actions = new StackPanel(); DockPanel.SetDock(actions, Dock.Bottom); sidebar.Children.Add(actions);
        actions.Children.Add(new ScrollViewer { Content = detail, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (editing) { actions.Children.Add(clear); actions.Children.Add(save); }
        else if (canRegister) { actions.Children.Add(complete); actions.Children.Add(omit); actions.Children.Add(report); }
        actions.Children.Add(Tool("Cerrar", Close)); sidebar.Children.Add(list);
        var itemText = new FrameworkElementFactory(typeof(TextBlock));
        itemText.SetBinding(TextBlock.TextProperty, new Binding("Label"));
        itemText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        itemText.SetValue(FrameworkElement.MaxWidthProperty, 250.0);
        list.ItemTemplate = new DataTemplate { VisualTree = itemText };
        System.Windows.Automation.AutomationProperties.SetName(list, "Puntos del croquis");
        root.Children.Add(viewport);
        var image = new BitmapImage(new Uri("pack://application:,,,/Ftracker;component/Assets/croquis-fime-limpio-v1.png"));
        map.Width = 1000; map.Height = 1000.0 * image.PixelHeight / image.PixelWidth;
        map.Children.Add(new Image { Source = image, Stretch = Stretch.Fill }); map.Children.Add(overlay);
        RenderOptions.SetBitmapScalingMode(map, BitmapScalingMode.HighQuality);
        viewport.Content = map;
        zoom.ValueChanged += (_, _) => map.LayoutTransform = new ScaleTransform(zoom.Value, zoom.Value);
        list.SelectionChanged += (_, _) => { Draw(); UpdateActions(); };
        overlay.MouseLeftButtonDown += (_, e) =>
        {
            if (!editing || busy || Selected is null) return;
            var position = e.GetPosition(overlay);
            PlaceSelectedPoint(Math.Clamp(position.X / map.Width, 0, 1), Math.Clamp(position.Y / map.Height, 0, 1));
        };
        clear.Click += (_, _) => { if (Selected is { } p) ChangeLocation(p, null, null); };
        save.Click += async (_, _) => await Run(async () =>
        {
            await store.SaveRouteCroquisAsync(targetId, this.points);
            this.points = (await store.GetRouteCroquisAsync(targetId)).ToList(); dirty = false;
            Rebind(); status.Text = "Ubicaciones guardadas. Se usarán al iniciar nuevos rondines.";
        });
        complete.Click += async (_, _) => await Run(() => Mark(RegistroResultado.Completado));
        omit.Click += async (_, _) => await Run(() => Mark(RegistroResultado.Omitido));
        report.Click += async (_, _) => await Run(ReportIncident);
        Loaded += (_, _) => { Fit(); list.Focus(); };
        Closing += (_, e) =>
        {
            if (busy) { e.Cancel = true; return; }
            if (dirty && MessageBox.Show(this, "Hay ubicaciones sin guardar. ¿Quieres descartarlas y cerrar?", "Ftracker",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) e.Cancel = true;
        };
        Rebind();
    }

    private Button Tool(string label, Action action)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("SecondaryButton") };
        button.Click += (_, _) => action(); return button;
    }

    private void Fit() => zoom.Value = Math.Clamp(Math.Min(Math.Max(1, viewport.ViewportWidth - 24) / map.Width,
        Math.Max(1, viewport.ViewportHeight - 24) / map.Height), zoom.Minimum, zoom.Maximum);

    internal void PlaceSelectedPoint(double x, double y)
    {
        if (editing && !busy && Selected is { } p) ChangeLocation(p, x, y);
    }

    private void ChangeLocation(CroquisPunto point, double? x, double? y)
    {
        points[points.FindIndex(p => p.Id == point.Id)] = point with { X = x, Y = y };
        dirty = true; Rebind(point.Id); status.Text = "Cambios sin guardar.";
    }

    private void Rebind(Guid? selectedId = null)
    {
        selectedId ??= Selected?.Id;
        list.ItemsSource = points.Select(p => new PointItem(p,
            $"{p.Orden}. {p.Nombre}\n{(editing ? (p.X.HasValue ? "Ubicado" : "Sin ubicación") : FriendlyValueConverter.Label(p.Resultado) + (p.X.HasValue ? "" : " · Sin ubicación"))}")).ToList();
        list.SelectedItem = list.Items.Cast<PointItem>().FirstOrDefault(x => x.Point.Id == selectedId) ?? list.Items.Cast<PointItem>().FirstOrDefault();
        Draw(); UpdateActions();
        if (points.Count == 0) status.Text = "Este recorrido no tiene puntos para mostrar.";
    }

    private void Draw()
    {
        overlay.Children.Clear();
        // Do not connect across missing locations: that would suggest an incorrect route.
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1]; var b = points[i];
            if (a.X is not double ax || a.Y is not double ay || b.X is not double bx || b.Y is not double by) continue;
            overlay.Children.Add(new Line { X1 = ax * map.Width, Y1 = ay * map.Height, X2 = bx * map.Width, Y2 = by * map.Height,
                Stroke = ThemeManager.Get("Muted"), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false });
        }
        foreach (var p in points.Where(x => x.X.HasValue && x.Y.HasValue))
        {
            var color = p.Resultado switch { RegistroResultado.Completado => "#237448", RegistroResultado.Omitido => "#A65D00", _ => "#245F9D" };
            var selected = Selected?.Id == p.Id;
            var marker = new Button
            {
                Content = p.Orden.ToString(), Width = 36, Height = 36, MinHeight = 36, Padding = new Thickness(0), Margin = new Thickness(0),
                Background = (Brush)new BrushConverter().ConvertFromString(color)!, Foreground = Brushes.White,
                BorderBrush = selected ? Brushes.Black : Brushes.White, BorderThickness = new Thickness(selected ? 3 : 2),
                ToolTip = $"{p.Orden}. {p.Nombre} · {FriendlyValueConverter.Label(p.Resultado)}"
            };
            System.Windows.Automation.AutomationProperties.SetName(marker, $"Punto {p.Orden}: {p.Nombre}");
            // Handle before bubbling to the map; selecting a marker must not move another point.
            marker.PreviewMouseLeftButtonDown += (_, e) => { Select(p.Id); e.Handled = true; };
            marker.Click += (_, _) => Select(p.Id);
            Canvas.SetLeft(marker, p.X!.Value * map.Width - 18); Canvas.SetTop(marker, p.Y!.Value * map.Height - 18);
            overlay.Children.Add(marker);
        }
    }

    private void Select(Guid id) => list.SelectedItem = list.Items.Cast<PointItem>().FirstOrDefault(x => x.Point.Id == id);

    private void UpdateActions()
    {
        var p = Selected;
        detail.Text = p is null ? "Selecciona un punto." : $"{p.Orden}. {p.Nombre}\n{FriendlyValueConverter.Label(p.Resultado)}\n" +
            (p.X.HasValue ? "Ubicación definida en el croquis." : "Sin ubicación. Puede registrarse desde esta lista.") +
            (string.IsNullOrWhiteSpace(p.Observaciones) ? "" : "\nObservación: " + p.Observaciones);
        save.IsEnabled = !busy && dirty;
        clear.IsEnabled = !busy && p?.X.HasValue == true;
        complete.IsEnabled = omit.IsEnabled = !busy && canRegister && p?.Resultado == RegistroResultado.Pendiente;
        report.IsEnabled = !busy && canRegister && p is not null;
        list.IsEnabled = !busy;
    }

    private async Task Mark(RegistroResultado result)
    {
        if (!canRegister || Selected is not { Resultado: RegistroResultado.Pendiente } point) return;
        var editor = new Editor(this, result == RegistroResultado.Omitido ? "Omitir punto" : "Completar punto");
        editor.Label(point.Nombre);
        var code = result == RegistroResultado.Completado && await store.RequiresCodeAsync(point.Id) ? editor.Text("Código del punto (escanéalo con el lector o escríbelo)") : null;
        var notes = editor.Text(result == RegistroResultado.Omitido ? "Observación (obligatoria)" : "Observación (opcional)");
        if (!editor.Save(async () =>
        {
            await using var db = store.Open();
            await new RondinService(db, store.Session).MarcarPuntoAsync(targetId, point.Id, new(result, notes.Text, code?.Text), store.Session.UserId!);
        })) return;
        var round = await store.GetRoundCroquisAsync(targetId);
        points = round.Puntos.ToList(); canRegister = round.PuedeRegistrar; Rebind(point.Id);
        status.Text = "Punto registrado.";
    }

    private async Task ReportIncident()
    {
        if (!canRegister || Selected is not { } point) return;
        await using var db = store.Open();
        var record = await db.RegistrosPunto.AsNoTracking().SingleOrDefaultAsync(x => x.RondinId == targetId && x.Id == point.Id)
            ?? throw new ReglaNegocioException("El punto no pertenece a este rondín.");
        if (IncidentEditor.Report(this, store, targetId, record.PuntoControlId, "Punto: " + point.Nombre))
            status.Text = "Incidencia guardada. Puedes adjuntar evidencia desde el apartado Incidencias.";
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true; UpdateActions(); status.Foreground = ThemeManager.Get("Muted"); status.Text = "Procesando…";
        try { await action(); }
        catch (DbUpdateConcurrencyException) { status.Foreground = ThemeManager.Get("Danger"); status.Text = "La ruta cambió en otra ventana. Cierra y vuelve a abrir el croquis antes de guardar."; }
        catch (ReglaNegocioException ex) { status.Foreground = ThemeManager.Get("Danger"); status.Text = ex.Message; }
        catch (Exception) { status.Foreground = ThemeManager.Get("Danger"); status.Text = "No se pudo completar la operación. Tus ubicaciones sin guardar se conservan."; }
        finally { busy = false; UpdateActions(); }
    }
}
