using System.Windows;
using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;

namespace Ftracker.Desktop;

internal static class IncidentEditor
{
    public static bool Report(Window owner, IFtrackerStore store, Guid roundId, Guid? pointId, string context)
    {
        var dialog = new Editor(owner, "Reportar incidencia");
        dialog.Label(context);
        var type = dialog.Text("Tipo (por ejemplo: mantenimiento; máximo 80 caracteres)");
        type.MaxLength = 80;
        var priority = dialog.Choice("Prioridad", Enum.GetValues<IncidenciaPrioridad>());
        var description = dialog.Text("Describe lo que encontraste (máximo 4000 caracteres)", multiline: true);
        description.MaxLength = 4000;
        dialog.Label("Después de guardar puedes adjuntar fotografías o PDF desde Incidencias → Adjuntar evidencia.");
        return dialog.Save(async () =>
        {
            await store.CreateIncidentAsync(new(roundId, pointId, type.Text, (IncidenciaPrioridad)priority.SelectedItem, description.Text));
        });
    }
}
