using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;
using Microsoft.Win32;

namespace Ftracker.Desktop;

// Alertas de atrasos, administración de usuarios, reportes y respaldos.
public sealed partial class MainWindow
{
    private readonly TextBlock alertBanner = new() { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };

    private void AddAlertBanner(DockPanel root)
    {
        alertBanner.Foreground = ThemeManager.Get("Danger");
        DockPanel.SetDock(alertBanner, Dock.Top); root.Children.Add(alertBanner);
        // Cada minuto vuelve a evaluar los atrasos con la hora actual, sin recargar la base de datos.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) =>
        {
            if (busy) return;
            try { rounds.Items.Refresh(); } catch (InvalidOperationException) { }
            UpdateAlertBanner();
        };
        timer.Start(); Closed += (_, _) => timer.Stop();
    }

    private void UpdateAlertBanner()
    {
        var late = vm.Rounds.Count(x => x.Alerta.Length > 0);
        var urgent = vm.IsAdmin ? vm.Incidents.Count(x => (x.Estado is IncidenciaEstado.Abierta or IncidenciaEstado.EnRevision) && (x.Prioridad is IncidenciaPrioridad.Alta or IncidenciaPrioridad.Critica)) : 0;
        var parts = new List<string>();
        if (late > 0) parts.Add(vm.IsAdmin ? $"{late} rondín(es) atrasado(s): pasaron más de 15 minutos de su hora y no se han iniciado" : $"Tienes {late} rondín(es) atrasado(s). Inícialos en cuanto puedas");
        if (urgent > 0) parts.Add($"{urgent} incidencia(s) de prioridad alta o crítica sin resolver");
        alertBanner.Visibility = parts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        alertBanner.Text = parts.Count == 0 ? "" : "⚠ " + string.Join("  ·  ", parts) + ".";
    }

    private Button PasswordButton()
    {
        var button = ActionButton("Cambiar contraseña", ChangeMyPassword); button.Style = (Style)FindResource("SecondaryButton"); return button;
    }

    private Task ChangeMyPassword()
    {
        var d = new Editor(this, "Cambiar contraseña");
        var current = d.Password("Contraseña actual"); var next = d.Password("Contraseña nueva (mínimo 12 caracteres)"); var confirm = d.Password("Confirma la contraseña nueva");
        if (d.Save(async () =>
        {
            if (next.Password != confirm.Password) throw new ReglaNegocioException("Las contraseñas nuevas no coinciden.");
            await store.ChangeOwnPasswordAsync(current.Password, next.Password);
        })) status.Text = "Contraseña actualizada.";
        return Task.CompletedTask;
    }

    private async Task EditUser()
    {
        var u = Selected<UserRow>(users);
        var d = new Editor(this, "Editar usuario"); var name = d.Text("Nombre completo", u.Nombre); var email = d.Text("Correo", u.Correo);
        var role = d.Choice("Rol", new[] { Roles.Prefecto, Roles.Administrador }); role.SelectedItem = u.Rol;
        if (u.Id == store.Session.UserId) d.Label("Tu propio rol no se puede cambiar. Si cambias tu nombre o correo, se verá reflejado al volver a iniciar sesión.");
        if (d.Save(() => store.UpdateUserAsync(u.Id, name.Text, email.Text, (string)role.SelectedItem))) await Refresh();
    }

    private Task ResetUserPassword()
    {
        var u = Selected<UserRow>(users);
        var d = new Editor(this, "Restablecer contraseña"); d.Label($"Usuario: {u.Nombre} ({u.Correo}). Entrega la contraseña nueva por un medio seguro y pídele que la cambie al entrar.");
        var next = d.Password("Contraseña nueva (mínimo 12 caracteres)"); var confirm = d.Password("Confirma la contraseña nueva");
        if (d.Save(async () =>
        {
            if (next.Password != confirm.Password) throw new ReglaNegocioException("Las contraseñas nuevas no coinciden.");
            await store.ResetPasswordAsync(u.Id, next.Password);
        })) status.Text = $"Contraseña de {u.Nombre} restablecida.";
        return Task.CompletedTask;
    }

    private async Task EditRoutePoints()
    {
        var route = Selected<RouteRow>(routes);
        var window = await RoutePointsWindow.CreateAsync(store, route.Id, route.Nombre);
        window.Owner = this; window.ShowDialog(); await Refresh();
    }

    private void BuildReports()
    {
        var panel = NewTab("Reportes y respaldo", "Exporta información a CSV (se abre en Excel) y protege tus datos con respaldos.");
        panel.Children.Add(new TextBlock { Text = "Reportes", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var reports = new WrapPanel();
        reports.Children.Add(ActionButton("Exportar rondines", () => ExportReport(ReportKind.Rondines)));
        reports.Children.Add(ActionButton("Exportar puntos", () => ExportReport(ReportKind.Puntos)));
        reports.Children.Add(ActionButton("Exportar incidencias", () => ExportReport(ReportKind.Incidencias)));
        panel.Children.Add(reports);
        panel.Children.Add(Note("Rondines: resumen por recorrido. Puntos: detalle de cada punto registrado, con su verificación por código. Incidencias: incluye las horas hasta resolver. Las fechas están en hora local."));
        panel.Children.Add(new TextBlock { Text = "Respaldo y restauración", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 24, 0, 6) });
        var backups = new WrapPanel();
        backups.Children.Add(ActionButton("Crear respaldo", CreateBackup));
        backups.Children.Add(DangerButton("Restaurar respaldo", RestoreBackup, () => true));
        panel.Children.Add(backups);
        panel.Children.Add(Note("El respaldo es un .zip con la base de datos y todas las evidencias. No está cifrado: guárdalo en un lugar seguro. Al restaurar, se reemplazan todos los datos actuales y antes se guarda una copia de seguridad en: " + System.IO.Path.Combine(store.DataDirectory, "respaldos")));
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Get("Muted"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };

    private async Task ExportReport(ReportKind kind)
    {
        var label = kind switch { ReportKind.Rondines => "rondines", ReportKind.Puntos => "puntos", _ => "incidencias" };
        var d = new Editor(this, "Exportar " + label);
        d.Label("Filtra por fecha y prefecto, o déjalo vacío para incluir todo.");
        var since = d.Date("Desde (opcional)"); var until = d.Date("Hasta (opcional)");
        var prefects = new List<UserRow> { new("", "Todos los prefectos", "", Roles.Prefecto, true) };
        prefects.AddRange(vm.Users.Where(x => x.Rol == Roles.Prefecto));
        var prefect = d.Choice("Prefecto", prefects, "Nombre");
        if (!d.Accept()) return;
        var chosen = ((UserRow)prefect.SelectedItem).Id;
        var dialog = new SaveFileDialog { FileName = $"Ftracker-{label}-{DateTime.Now:yyyyMMdd}.csv", Filter = "CSV (Excel)|*.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var count = await store.ExportReportAsync(kind, dialog.FileName, since.SelectedDate, until.SelectedDate, chosen.Length > 0 ? chosen : null);
        status.Text = $"Reporte exportado: {count} fila(s).";
    }

    private async Task CreateBackup()
    {
        var dialog = new SaveFileDialog { FileName = $"Ftracker-respaldo-{DateTime.Now:yyyyMMdd-HHmm}.zip", Filter = "Respaldo de Ftracker|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        await store.CreateBackupAsync(dialog.FileName);
        status.Text = "Respaldo guardado.";
        MessageBox.Show(this, "Respaldo creado.\n\nIncluye usuarios (con contraseñas protegidas), incidencias y evidencias sin cifrar. Guárdalo en un lugar seguro.", "Ftracker", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RestoreBackup()
    {
        var dialog = new OpenFileDialog { Filter = "Respaldo de Ftracker|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        if (!Confirm("¿Restaurar este respaldo?\n\nSe reemplazarán TODOS los datos actuales (usuarios, rutas, rondines, incidencias y evidencias) por los del respaldo. Antes se guardará una copia de seguridad de lo actual en la subcarpeta respaldos de la carpeta de datos.\n\nFtracker se cerrará al terminar.", destructive: true)) return;
        await store.RestoreBackupAsync(dialog.FileName);
        MessageBox.Show(this, "Respaldo restaurado. Ftracker se cerrará: ábrelo de nuevo e inicia sesión con una cuenta del respaldo.", "Ftracker", MessageBoxButton.OK, MessageBoxImage.Information);
        System.Windows.Application.Current.Shutdown();
    }
}
