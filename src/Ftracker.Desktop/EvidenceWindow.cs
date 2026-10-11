using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;
using Microsoft.Win32;

namespace Ftracker.Desktop;

internal sealed class EvidenceWindow : Window
{
    private readonly LocalStore store;
    private readonly ComboBox files = new() { DisplayMemberPath = nameof(Evidencia.NombreOriginal), MinWidth = 260, MaxWidth = 650, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 600, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock percentage = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 55 };
    private readonly Slider zoom = new() { Minimum = 0.05, Maximum = 4, Value = 1, Width = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button export = new() { Content = "Exportar archivo" };
    private readonly List<Button> imageActions = [];
    private bool busy;
    private bool closed;

    public static async Task<EvidenceWindow> CreateAsync(LocalStore store, Guid incidentId)
    {
        var evidence = await store.GetEvidenceAsync(incidentId);
        if (evidence.Count == 0) throw new ReglaNegocioException("Esta incidencia no tiene archivos adjuntos.");
        return new EvidenceWindow(store, evidence);
    }

    private EvidenceWindow(LocalStore store, IReadOnlyList<Evidencia> evidence)
    {
        this.store = store;
        Title = "Evidencias · Ftracker"; Icon = Brand.Icon; Style = (Style)FindResource(typeof(Window));
        Width = 1000; Height = 740; MinWidth = 700; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Evidencias de la incidencia", FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = $"Archivos adjuntos: {evidence.Count}. Selecciona uno para revisarlo.", Margin = new Thickness(0, 6, 0, 6) });
        header.Children.Add(files);
        System.Windows.Automation.AutomationProperties.SetName(files, "Archivo de evidencia");
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; header.Children.Add(tools);
        tools.Children.Add(ImageTool("−", () => zoom.Value /= 1.25)); tools.Children.Add(zoom);
        tools.Children.Add(ImageTool("+", () => zoom.Value *= 1.25)); tools.Children.Add(percentage);
        tools.Children.Add(ImageTool("Ajustar", Fit)); tools.Children.Add(ImageTool("100 %", () => zoom.Value = 1));
        tools.Children.Add(export);
        var close = new Button { Content = "Cerrar", IsCancel = true, Style = (Style)FindResource("SecondaryButton") };
        close.Click += (_, _) => Close(); tools.Children.Add(close);
        System.Windows.Automation.AutomationProperties.SetName(zoom, "Zoom de evidencia");
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var preview = new Grid { Background = Brushes.White };
        viewport.Content = image; preview.Children.Add(viewport); preview.Children.Add(message);
        root.Children.Add(new Border { Child = preview, BorderThickness = new Thickness(1), BorderBrush = Brushes.LightSlateGray, CornerRadius = new CornerRadius(6) });
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        System.Windows.Automation.AutomationProperties.SetName(image, "Vista previa de evidencia");
        zoom.ValueChanged += (_, _) => { image.LayoutTransform = new ScaleTransform(zoom.Value, zoom.Value); percentage.Text = $"{zoom.Value:P0}"; };
        files.ItemsSource = evidence;
        files.SelectionChanged += async (_, _) => await LoadSelected();
        export.Click += async (_, _) => await ExportSelected();
        Closed += (_, _) => { closed = true; image.Source = null; };
        Loaded += (_, _) => { files.SelectedIndex = 0; files.Focus(); };
        UpdateActions();
    }

    private Button ImageTool(string label, Action action)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("SecondaryButton") };
        button.Click += (_, _) => action(); imageActions.Add(button); return button;
    }

    private async Task LoadSelected()
    {
        if (busy || files.SelectedItem is not Evidencia selected) return;
        busy = true; image.Source = null; image.Width = image.Height = double.NaN; message.Text = "Cargando archivo…";
        message.Visibility = Visibility.Visible; status.Text = selected.NombreOriginal; UpdateActions();
        try
        {
            var bytes = await store.ReadEvidenceAsync(selected.Id);
            if (closed) return;
            if (selected.TipoMime == "application/pdf")
            {
                message.Text = "La vista previa de PDF no está disponible. Usa Exportar archivo para guardar una copia."; return;
            }
            if (selected.TipoMime is not ("image/png" or "image/jpeg"))
            {
                message.Text = "Este formato no tiene vista previa. Puedes exportar el archivo."; return;
            }
            var bitmap = await Task.Run(() => Decode(bytes));
            if (closed) return;
            image.Source = bitmap; image.Width = bitmap.PixelWidth; image.Height = bitmap.PixelHeight;
            message.Visibility = Visibility.Collapsed;
            viewport.ScrollToHorizontalOffset(0); viewport.ScrollToVerticalOffset(0);
            viewport.UpdateLayout(); Fit();
            status.Text = selected.NombreOriginal + " · Vista previa disponible. El archivo original no se modifica.";
        }
        catch (ReglaNegocioException ex) { message.Text = ex.Message; }
        catch (Exception) { message.Text = "No se pudo mostrar la imagen. Puede estar dañada o tener dimensiones no compatibles."; }
        finally { busy = false; if (!closed) UpdateActions(); }
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || frame.PixelWidth > 20000 || frame.PixelHeight > 20000 || (long)frame.PixelWidth * frame.PixelHeight > 80000000)
            throw new InvalidDataException("Dimensiones no compatibles.");
        var width = Math.Min(frame.PixelWidth, 2400); var height = frame.PixelHeight;
        // Decode a bounded preview; 100% means preview size, never rewrite the original.
        input.Position = 0;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (frame.PixelWidth >= height) bitmap.DecodePixelWidth = width;
        else bitmap.DecodePixelHeight = Math.Min(height, 2400);
        bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    private void Fit()
    {
        if (image.Source is null) return;
        zoom.Value = Math.Clamp(Math.Min(Math.Max(1, viewport.ViewportWidth - 20) / image.Width,
            Math.Max(1, viewport.ViewportHeight - 20) / image.Height), zoom.Minimum, 1);
        percentage.Text = $"{zoom.Value:P0}";
    }

    private void UpdateActions()
    {
        var canZoom = !busy && image.Source is not null;
        zoom.IsEnabled = canZoom; foreach (var button in imageActions) button.IsEnabled = canZoom;
        export.IsEnabled = !busy && files.SelectedItem is Evidencia; files.IsEnabled = !busy;
    }

    private async Task ExportSelected()
    {
        if (busy || files.SelectedItem is not Evidencia selected) return;
        var target = new SaveFileDialog { FileName = Path.GetFileName(selected.NombreOriginal), Filter = "Archivo|*.*" };
        if (target.ShowDialog(this) != true) return;
        busy = true; UpdateActions();
        try { var bytes = await store.ReadEvidenceAsync(selected.Id); await File.WriteAllBytesAsync(target.FileName, bytes); status.Text = "Copia exportada. El archivo original se conserva."; }
        catch (ReglaNegocioException ex) { status.Text = ex.Message; }
        catch (Exception) { status.Text = "No se pudo exportar. Revisa la ubicación de destino y sus permisos."; }
        finally { busy = false; if (!closed) UpdateActions(); }
    }
}
