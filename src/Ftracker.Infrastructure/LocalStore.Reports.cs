using System.Text;
using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public enum ReportKind { Rondines, Puntos, Incidencias }

public sealed partial class LocalStore
{
    private static DateTime LocalDayToUtc(DateTime day) => DateTime.SpecifyKind(day.Date, DateTimeKind.Local).ToUniversalTime();
    private static string Stamp(DateTime? utc) => utc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";

    /// <summary>Exporta un reporte a CSV (UTF-8 con BOM, para que Excel respete los acentos). Fechas en hora local. Devuelve las filas escritas.</summary>
    public async Task<int> ExportReportAsync(ReportKind kind, string path, DateTime? desde, DateTime? hasta, string? prefectoId)
    {
        RequireAdmin();
        if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date) throw new ReglaNegocioException("La fecha final no puede ser anterior a la inicial.");
        var from = desde.HasValue ? LocalDayToUtc(desde.Value) : (DateTime?)null;
        var to = hasta.HasValue ? LocalDayToUtc(hasta.Value.AddDays(1)) : (DateTime?)null;
        await using var db = Open();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.NombreCompleto);
        string Who(string id) => users.GetValueOrDefault(id, "Usuario");
        var rows = new List<string?[]>();
        string?[] header;

        if (kind == ReportKind.Incidencias)
        {
            header = ["Fecha", "Ruta", "Prefecto", "Punto", "Tipo", "Prioridad", "Estado", "Descripción", "Resuelta en", "Horas hasta resolver", "Evidencias"];
            var query = db.Incidencias.AsNoTracking().Include(x => x.Rondin).ThenInclude(x => x.Ruta).Include(x => x.Evidencias).AsQueryable();
            if (from.HasValue) query = query.Where(x => x.CreadoEn >= from.Value);
            if (to.HasValue) query = query.Where(x => x.CreadoEn < to.Value);
            if (!string.IsNullOrEmpty(prefectoId)) query = query.Where(x => x.Rondin.PrefectoId == prefectoId);
            var list = await query.OrderBy(x => x.CreadoEn).ToListAsync();
            var roundIds = list.Select(x => x.RondinId).Distinct().ToArray();
            var names = (await db.RegistrosPunto.AsNoTracking().Where(x => roundIds.Contains(x.RondinId)).Select(x => new { x.RondinId, x.PuntoControlId, x.PuntoNombre }).ToListAsync())
                .ToDictionary(x => (x.RondinId, x.PuntoControlId), x => x.PuntoNombre);
            foreach (var i in list)
                rows.Add([Stamp(i.CreadoEn), i.Rondin.Ruta.Nombre, Who(i.Rondin.PrefectoId),
                    i.PuntoControlId.HasValue ? names.GetValueOrDefault((i.RondinId, i.PuntoControlId.Value), "") : "General",
                    i.Tipo, i.Prioridad.ToString(), i.Estado.ToString(), i.Descripcion, Stamp(i.ResueltaEn),
                    i.ResueltaEn.HasValue ? Math.Round((i.ResueltaEn.Value - i.CreadoEn).TotalHours, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
                    i.Evidencias.Count.ToString()]);
        }
        else
        {
            var query = db.Rondines.AsNoTracking().Include(x => x.Ruta).Include(x => x.Registros).Include(x => x.Incidencias).AsQueryable();
            if (from.HasValue) query = query.Where(x => x.ProgramadoPara >= from.Value);
            if (to.HasValue) query = query.Where(x => x.ProgramadoPara < to.Value);
            if (!string.IsNullOrEmpty(prefectoId)) query = query.Where(x => x.PrefectoId == prefectoId);
            var list = await query.OrderBy(x => x.ProgramadoPara).ToListAsync();
            if (kind == ReportKind.Rondines)
            {
                header = ["Programado", "Ruta", "Prefecto", "Estado", "Iniciado", "Cerrado", "Minutos de recorrido", "Puntos", "Completados", "Omitidos", "Pendientes", "Verificados por código", "Incidencias", "Observaciones"];
                foreach (var r in list)
                    rows.Add([Stamp(r.ProgramadoPara), r.Ruta.Nombre, Who(r.PrefectoId), r.Estado.ToString(), Stamp(r.IniciadoEn), Stamp(r.CerradoEn),
                        r.IniciadoEn.HasValue && r.CerradoEn.HasValue ? Math.Round((r.CerradoEn.Value - r.IniciadoEn.Value).TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
                        r.Registros.Count.ToString(), r.Registros.Count(x => x.Resultado == RegistroResultado.Completado).ToString(),
                        r.Registros.Count(x => x.Resultado == RegistroResultado.Omitido).ToString(), r.Registros.Count(x => x.Resultado == RegistroResultado.Pendiente).ToString(),
                        r.Registros.Count(x => x.Verificado).ToString(), r.Incidencias.Count.ToString(), r.Observaciones]);
            }
            else
            {
                header = ["Rondín programado", "Ruta", "Prefecto", "Orden", "Punto", "Resultado", "Marcado", "Verificado por código", "Observaciones"];
                foreach (var r in list)
                    foreach (var p in r.Registros.OrderBy(x => x.Orden))
                        rows.Add([Stamp(r.ProgramadoPara), r.Ruta.Nombre, Who(r.PrefectoId), p.Orden.ToString(), p.PuntoNombre, p.Resultado.ToString(), Stamp(p.MarcadoEn), p.Verificado ? "Sí" : "No", p.Observaciones]);
            }
        }

        var csv = new StringBuilder();
        csv.AppendLine(CsvLine(header));
        foreach (var row in rows) csv.AppendLine(CsvLine(row));
        await File.WriteAllTextAsync(path, csv.ToString(), new UTF8Encoding(true));
        Audit(db, "ExportarReporte", kind.ToString(), $"{rows.Count} fila(s)");
        await db.SaveChangesAsync();
        return rows.Count;
    }

    private static string CsvLine(IEnumerable<string?> cells) => string.Join(",", cells.Select(CsvCell));

    /// <summary>Escapa comillas y neutraliza fórmulas: el texto libre que empieza con = + - @ se prefija con apóstrofo para que Excel no lo ejecute.</summary>
    internal static string CsvCell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
