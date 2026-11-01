using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ftracker.Application;
using Microsoft.Win32;

namespace Ftracker.Desktop;

internal sealed class ClosurePhotoPicker : StackPanel
{
    private readonly Image preview = new() { Height = 210, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock selection = new() { Text = "Ninguna fotografía seleccionada.", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock error = new() { Foreground = ThemeManager.Get("Danger"), TextWrapping = TextWrapping.Wrap };
    private readonly Button choose = new() { Content = "Seleccionar fotografía", HorizontalAlignment = HorizontalAlignment.Left };
    private FotoCierreCommand? photo;
    internal FotoCierreCommand? Photo => photo;

    public ClosurePhotoPicker(Window owner)
    {
        Children.Add(new TextBlock { Text = "Fotografía de la hoja de firmas (obligatoria)", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        Children.Add(new TextBlock { Text = "JPG o PNG, hasta 5 MB. Comprueba que se lean la fecha y las firmas de este rondín.", TextWrapping = TextWrapping.Wrap });
        Children.Add(choose); Children.Add(selection); Children.Add(preview); Children.Add(error);
        System.Windows.Automation.AutomationProperties.SetName(choose, "Seleccionar fotografía de firmas");
        System.Windows.Automation.AutomationProperties.SetName(preview, "Vista previa de hoja de firmas");
        choose.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Fotografía de la hoja de firmas", Filter = "Fotografías JPG o PNG|*.jpg;*.jpeg;*.png", Multiselect = false };
            if (dialog.ShowDialog(owner) == true) await LoadAsync(dialog.FileName);
        };
    }

    internal async Task<bool> LoadAsync(string path)
    {
        photo = null; preview.Source = null; preview.Visibility = Visibility.Collapsed;
        error.Text = ""; selection.Text = "Cargando fotografía…"; choose.IsEnabled = false;
        try
        {
            if (Path.GetExtension(path).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png"))
                throw new ReglaNegocioException("Selecciona una fotografía JPG o PNG.");
            byte[] bytes;
            using (var input = File.OpenRead(path))
            {
                if (input.Length is <= 0 or > 5 * 1024 * 1024) throw new ReglaNegocioException("La fotografía debe tener contenido y no superar 5 MB.");
                bytes = new byte[(int)input.Length]; await input.ReadExactlyAsync(bytes);
                if (input.ReadByte() != -1) throw new IOException("El archivo cambió mientras se leía.");
            }
            var bitmap = await Task.Run(() => EvidenceWindow.Decode(bytes));
            preview.Source = bitmap; preview.Visibility = Visibility.Visible;
            photo = new FotoCierreCommand(Path.GetFileName(path), bytes);
            selection.Text = $"{photo.NombreArchivo} · {bytes.Length / 1024.0:N0} KB";
            return true;
        }
        catch (ReglaNegocioException ex) { error.Text = ex.Message; }
        catch (Exception) { error.Text = "No se pudo abrir la fotografía. Puede estar dañada o no estar disponible en este equipo."; }
        finally { choose.IsEnabled = true; }
        selection.Text = "Ninguna fotografía seleccionada.";
        return false;
    }
}
