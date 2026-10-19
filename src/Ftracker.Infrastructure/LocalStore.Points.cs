using System.Net;
using System.Security.Cryptography;
using System.Text;
using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Ftracker.Infrastructure;

public sealed record PuntoCatalogo(Guid Id, int Orden, string Nombre, bool ConCodigo);

public sealed partial class LocalStore
{
    // Sin I, O, 0 ni 1 para que el código se pueda leer e imprimir sin ambigüedades.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int MaxPointName = 120;

    public async Task<IReadOnlyList<PuntoCatalogo>> GetRoutePointsAsync(Guid rutaId)
    {
        RequireAdmin();
        await using var db = Open();
        return await db.PuntosControl.AsNoTracking().Where(x => x.RutaId == rutaId).OrderBy(x => x.Orden)
            .Select(x => new PuntoCatalogo(x.Id, x.Orden, x.Nombre, x.CodigoVerificacion != null)).ToListAsync();
    }

    public async Task AddPointAsync(Guid rutaId, string name)
    {
        RequireAdmin();
        name = ValidatePointName(name);
        await using var db = Open();
        if (!await db.Rutas.AnyAsync(x => x.Id == rutaId)) throw new ReglaNegocioException("La ruta ya no existe. Actualiza los datos.");
        var last = await db.PuntosControl.Where(x => x.RutaId == rutaId).MaxAsync(x => (int?)x.Orden) ?? 0;
        // Si la ruta ya usa códigos, el punto nuevo también recibe uno para no dejar huecos en la verificación.
        var usesCodes = await db.PuntosControl.AnyAsync(x => x.RutaId == rutaId && x.CodigoVerificacion != null);
        var point = new PuntoControl { RutaId = rutaId, Nombre = name, Orden = last + 1, CreadoPorId = Session.UserId, CodigoVerificacion = usesCodes ? NewCode() : null };
        db.PuntosControl.Add(point);
        Audit(db, "AgregarPunto", point.Id.ToString(), $"Ruta {rutaId}: {name}");
        await db.SaveChangesAsync();
    }

    public async Task RenamePointAsync(Guid pointId, string name)
    {
        RequireAdmin();
        name = ValidatePointName(name);
        await using var db = Open();
        var point = await db.PuntosControl.SingleOrDefaultAsync(x => x.Id == pointId) ?? throw new ReglaNegocioException("El punto ya no existe. Actualiza los datos.");
        if (point.Nombre == name) return;
        var previous = point.Nombre;
        point.Nombre = name; point.ActualizadoEn = DateTime.UtcNow;
        Audit(db, "RenombrarPunto", pointId.ToString(), $"{previous} → {name}");
        await db.SaveChangesAsync();
    }

    /// <summary>Sube (-1) o baja (+1) un punto. Los rondines ya iniciados conservan el orden con que se iniciaron.</summary>
    public async Task MovePointAsync(Guid pointId, int direction)
    {
        RequireAdmin();
        if (direction != -1 && direction != 1) throw new ReglaNegocioException("Movimiento no válido.");
        await using var db = Open();
        var point = await db.PuntosControl.SingleOrDefaultAsync(x => x.Id == pointId) ?? throw new ReglaNegocioException("El punto ya no existe. Actualiza los datos.");
        var siblings = await db.PuntosControl.Where(x => x.RutaId == point.RutaId).OrderBy(x => x.Orden).ToListAsync();
        var target = siblings.FindIndex(x => x.Id == pointId) + direction;
        if (target < 0 || target >= siblings.Count) return;
        var other = siblings[target];
        int first = point.Orden, second = other.Orden;
        // El índice (RutaId, Orden) es único: se intercambia pasando por un valor temporal dentro de una transacción.
        await using var transaction = await db.Database.BeginTransactionAsync();
        point.Orden = -1; await db.SaveChangesAsync();
        other.Orden = first; await db.SaveChangesAsync();
        point.Orden = second;
        Audit(db, "MoverPunto", pointId.ToString(), $"Orden {first} → {second}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    /// <summary>Activa la verificación por código en todos los puntos de la ruta (solo crea los faltantes, salvo que se pida regenerar) o la desactiva.</summary>
    public async Task SetRouteCodesAsync(Guid rutaId, bool enabled, bool regenerate = false)
    {
        RequireAdmin();
        await using var db = Open();
        var points = await db.PuntosControl.Where(x => x.RutaId == rutaId).ToListAsync();
        if (points.Count == 0) throw new ReglaNegocioException("La ruta no tiene puntos.");
        foreach (var point in points)
        {
            var code = !enabled ? null : regenerate || string.IsNullOrEmpty(point.CodigoVerificacion) ? NewCode() : point.CodigoVerificacion;
            if (code == point.CodigoVerificacion) continue;
            point.CodigoVerificacion = code; point.ActualizadoEn = DateTime.UtcNow;
        }
        Audit(db, !enabled ? "DesactivarCodigosRuta" : regenerate ? "RegenerarCodigosRuta" : "ActivarCodigosRuta", rutaId.ToString());
        await db.SaveChangesAsync();
    }

    /// <summary>Indica si el punto de un registro de rondín exige código. Nunca devuelve el código.</summary>
    public async Task<bool> RequiresCodeAsync(Guid registroId)
    {
        if (Session.UserId is null) throw new ReglaNegocioException("Inicia sesión para continuar.");
        await using var db = Open();
        return await db.RegistrosPunto.AsNoTracking().Where(x => x.Id == registroId)
            .Join(db.PuntosControl, r => r.PuntoControlId, p => p.Id, (r, p) => p.CodigoVerificacion)
            .AnyAsync(c => c != null);
    }

    /// <summary>Genera una hoja HTML imprimible con el código y el QR de cada punto. El QR contiene solo el código, así que un lector USB lo "teclea" en el formulario.</summary>
    public async Task<int> WriteCodeSheetAsync(Guid rutaId, string path)
    {
        RequireAdmin();
        await using var db = Open();
        var route = await db.Rutas.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rutaId) ?? throw new ReglaNegocioException("La ruta ya no existe. Actualiza los datos.");
        var points = await db.PuntosControl.AsNoTracking().Where(x => x.RutaId == rutaId && x.CodigoVerificacion != null).OrderBy(x => x.Orden).ToListAsync();
        if (points.Count == 0) throw new ReglaNegocioException("Activa primero la verificación por código en esta ruta.");
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Códigos de verificación</title><style>")
            .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#111}h1{font-size:20px}p.note{color:#555;font-size:12px}")
            .Append(".grid{display:grid;grid-template-columns:repeat(2,1fr);gap:14px}.card{border:1px solid #999;border-radius:8px;padding:12px;text-align:center;break-inside:avoid}")
            .Append(".card img{width:150px;height:150px;image-rendering:pixelated}.code{font:700 26px Consolas,monospace;letter-spacing:2px}")
            .Append("@media print{body{margin:8mm}}</style></head><body>");
        html.Append("<h1>").Append(WebUtility.HtmlEncode(route.Nombre)).Append(" · Códigos de verificación</h1>")
            .Append("<p class=\"note\">Imprime y coloca cada código en su punto de control. Si un código se compromete, genera otros desde Ftracker.</p><div class=\"grid\">");
        foreach (var point in points)
        {
            html.Append("<div class=\"card\"><div>").Append(point.Orden).Append(". ").Append(WebUtility.HtmlEncode(point.Nombre)).Append("</div>")
                .Append("<img alt=\"QR\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(QrPng(point.CodigoVerificacion!))).Append("\">")
                .Append("<div class=\"code\">").Append(WebUtility.HtmlEncode(point.CodigoVerificacion)).Append("</div></div>");
        }
        html.Append("</div></body></html>");
        await File.WriteAllTextAsync(path, html.ToString(), new UTF8Encoding(false));
        db.Auditorias.Add(new Auditoria { ActorId = Session.UserId!, Accion = "ImprimirCodigosRuta", Entidad = "Catalogo", EntidadId = rutaId.ToString() });
        await db.SaveChangesAsync();
        return points.Count;
    }

    private static byte[] QrPng(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(6);
    }

    private static string NewCode()
    {
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars, 0, 4) + "-" + new string(chars, 4, 4);
    }

    private static string ValidatePointName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > MaxPointName) throw new ReglaNegocioException($"Escribe un nombre de punto de 1 a {MaxPointName} caracteres.");
        return name;
    }
}
