using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ftracker.Application;
using Ftracker.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace Ftracker.Desktop;

/// <summary>Edición de los puntos de una ruta: agregar, renombrar, reordenar y administrar los códigos de verificación.</summary>
internal sealed class RoutePointsWindow : Window
{
    private sealed record Row(Guid Id, int Orden, string Nombre, bool ConCodigo)
    {
        public string Verificacion => ConCodigo ? "Con código" : "Sin código";
    }

    private readonly LocalStore store;
    private readonly Guid routeId;
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, Height = 300 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly List<(Button Button, Func<bool> Enabled)> actions = [];
    private bool busy;
    private Row? Current => grid.SelectedItem as Row;

    public static async Task<RoutePointsWindow> CreateAsync(LocalStore store, Guid routeId, string name)
    {
        var window = new RoutePointsWindow(store, routeId, name);
        await window.LoadAsync(null, false);
        return window;
    }

    private RoutePointsWindow(LocalStore store, Guid routeId, string name)
    {
        this.store = store; this.routeId = routeId;
        Style = (Style)FindResource(typeof(Window)); Background = ThemeManager.Get("Page");
        Title = "Puntos de " + name; Width = 780; Height = 660; MinWidth = 640; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(22) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = name, FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Los cambios de nombre u orden se aplican a los rondines que aún no se han iniciado; los ya iniciados conservan lo que tenían. Con verificación por código, el prefecto debe escanear (lector USB) o escribir el código del punto para completarlo.",
            TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Get("Muted"), Margin = new Thickness(0, 6, 0, 16)
        });
        var points = new WrapPanel();
        points.Children.Add(Btn("Agregar punto", AddPoint));
        points.Children.Add(Btn("Renombrar", Rename, () => Current is not null));
        points.Children.Add(Btn("Subir", () => Move(-1), () => Current is not null && grid.SelectedIndex > 0));
        points.Children.Add(Btn("Bajar", () => Move(1), () => Current is not null && grid.SelectedIndex < grid.Items.Count - 1));
        panel.Children.Add(points);
        var codes = new WrapPanel();
        codes.Children.Add(Btn("Activar códigos", () => SetCodes(true, false), () => grid.Items.Count > 0));
        codes.Children.Add(Btn("Regenerar códigos", () => SetCodes(true, true), () => grid.Items.Cast<Row>().Any(x => x.ConCodigo)));
        codes.Children.Add(Btn("Desactivar códigos", () => SetCodes(false, false), () => grid.Items.Cast<Row>().Any(x => x.ConCodigo)));
        codes.Children.Add(Btn("Imprimir códigos (QR)", PrintCodes, () => grid.Items.Cast<Row>().Any(x => x.ConCodigo)));
        panel.Children.Add(codes);
        foreach (var (header, path) in new[] { ("N.º", nameof(Row.Orden)), ("Punto", nameof(Row.Nombre)), ("Verificación", nameof(Row.Verificacion)) })
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), MinWidth = path == nameof(Row.Orden) ? 60 : 120, Width = path == nameof(Row.Nombre) ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        grid.Margin = new Thickness(0, 10, 0, 0);
        grid.SelectionChanged += (_, _) => Sync();
        panel.Children.Add(grid);
        status.Foreground = ThemeManager.Get("Muted"); status.FontSize = 12; panel.Children.Add(status);
        var close = new Button { Content = "Cerrar", IsCancel = true, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(close);
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Sync();
    }

    private Button Btn(string title, Func<Task> action, Func<bool>? enabled = null)
    {
        var button = new Button { Content = title };
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        actions.Add((button, enabled ?? (() => true)));
        button.Click += async (_, _) => await Run(action);
        return button;
    }

    private void Sync() { foreach (var (button, enabled) in actions) button.IsEnabled = !busy && enabled(); }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true; Sync();
        try { await action(); }
        catch (DbUpdateConcurrencyException) { MessageBox.Show(this, "Los datos cambiaron en otra ventana. Se recargará la lista; intenta de nuevo.", "Ftracker"); await LoadAsync(null, false); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ftracker", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { busy = false; Sync(); }
    }

    private async Task LoadAsync(Guid? keep, bool selectLast)
    {
        var rows = (await store.GetRoutePointsAsync(routeId)).Select(x => new Row(x.Id, x.Orden, x.Nombre, x.ConCodigo)).ToList();
        grid.ItemsSource = rows;
        grid.SelectedItem = selectLast ? rows.LastOrDefault() : rows.FirstOrDefault(x => x.Id == keep);
        Sync();
    }

    private async Task AddPoint()
    {
        var d = new Editor(this, "Agregar punto"); var name = d.Text("Nombre del punto (máximo 120 caracteres)"); name.MaxLength = 120;
        if (d.Save(() => store.AddPointAsync(routeId, name.Text))) { await LoadAsync(null, true); status.Text = "Punto agregado al final de la ruta."; }
    }

    private async Task Rename()
    {
        var row = Current ?? throw new ReglaNegocioException("Selecciona un punto.");
        var d = new Editor(this, "Renombrar punto"); var name = d.Text("Nombre del punto (máximo 120 caracteres)", row.Nombre); name.MaxLength = 120;
        if (d.Save(() => store.RenamePointAsync(row.Id, name.Text))) { await LoadAsync(row.Id, false); status.Text = "Punto renombrado."; }
    }

    private async Task Move(int direction)
    {
        var row = Current ?? throw new ReglaNegocioException("Selecciona un punto.");
        await store.MovePointAsync(row.Id, direction); await LoadAsync(row.Id, false);
    }

    private async Task SetCodes(bool enabled, bool regenerate)
    {
        var message = !enabled ? "¿Desactivar la verificación por código? Los prefectos podrán completar los puntos sin escanear nada."
            : regenerate ? "¿Generar códigos nuevos para todos los puntos? Los códigos ya impresos dejarán de funcionar."
            : "¿Activar la verificación por código? Se creará un código para cada punto que aún no lo tenga. Después imprime la hoja de códigos y colócalos en cada punto, o los prefectos no podrán completarlos.";
        if (MessageBox.Show(this, message, "Confirmar · Ftracker", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await store.SetRouteCodesAsync(routeId, enabled, regenerate); await LoadAsync(Current?.Id, false);
        status.Text = !enabled ? "Verificación por código desactivada." : "Códigos listos. Usa Imprimir códigos (QR) para generar la hoja.";
    }

    private async Task PrintCodes()
    {
        var dialog = new SaveFileDialog { FileName = $"Codigos-{DateTime.Now:yyyyMMdd}.html", Filter = "Hoja imprimible (HTML)|*.html" };
        if (dialog.ShowDialog(this) != true) return;
        var count = await store.WriteCodeSheetAsync(routeId, dialog.FileName);
        status.Text = $"Hoja con {count} código(s) guardada. Ábrela con tu navegador e imprímela; contiene los códigos, así que guárdala con cuidado.";
    }
}
