using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public sealed partial class LocalStore
{
    public async Task<IReadOnlyList<CroquisPunto>> GetRouteCroquisAsync(Guid rutaId)
    {
        RequireAdmin();
        await using var db = Open();
        if (!await db.Rutas.AnyAsync(x => x.Id == rutaId)) throw new ReglaNegocioException("La ruta no existe.");
        var points = await db.PuntosControl.AsNoTracking().Where(x => x.RutaId == rutaId && x.Activo).OrderBy(x => x.Orden).ToListAsync();
        var ids = points.Select(x => x.Id).ToArray();
        var locations = await db.UbicacionesCroquis.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        return points.Select(x => new CroquisPunto(x.Id, x.Orden, x.Nombre, locations.GetValueOrDefault(x.Id)?.X,
            locations.GetValueOrDefault(x.Id)?.Y, Version: x.RowVersion)).ToList();
    }

    public async Task SaveRouteCroquisAsync(Guid rutaId, IReadOnlyList<CroquisPunto> points)
    {
        RequireAdmin();
        if (points.Any(p => p.X.HasValue != p.Y.HasValue || p.X.HasValue &&
            (!double.IsFinite(p.X.Value) || !double.IsFinite(p.Y!.Value) || p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1)))
            throw new ReglaNegocioException("Las posiciones deben estar dentro del croquis.");
        await using var db = Open();
        var route = await db.Rutas.Include(x => x.Puntos).SingleOrDefaultAsync(x => x.Id == rutaId)
            ?? throw new ReglaNegocioException("La ruta no existe.");
        var active = route.Puntos.Where(x => x.Activo).ToList();
        if (points.Select(x => x.Id).Distinct().Count() != points.Count ||
            !active.Select(x => x.Id).ToHashSet().SetEquals(points.Select(x => x.Id)))
            throw new ReglaNegocioException("Los puntos de la ruta cambiaron. Cierra y vuelve a abrir el croquis.");
        var ids = points.Select(x => x.Id).ToArray();
        var locations = await db.UbicacionesCroquis.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        foreach (var point in active)
        {
            var input = points.Single(x => x.Id == point.Id);
            if (!(point.RowVersion ?? []).SequenceEqual(input.Version ?? [])) throw new DbUpdateConcurrencyException();
            var current = locations.GetValueOrDefault(point.Id);
            if (input.X.HasValue)
            {
                if (current is null) db.UbicacionesCroquis.Add(new UbicacionCroquis { Id = point.Id, X = input.X.Value, Y = input.Y!.Value });
                else { current.X = input.X.Value; current.Y = input.Y!.Value; }
            }
            else if (current is not null) db.UbicacionesCroquis.Remove(current);
            point.ActualizadoEn = DateTime.UtcNow; // Optimistic concurrency for the whole map save.
        }
        Audit(db, "UbicarPuntosCroquis", rutaId.ToString());
        await db.SaveChangesAsync();
    }

    public async Task<CroquisRecorrido> GetRoundCroquisAsync(Guid rondinId)
    {
        await using var db = Open();
        var round = await db.Rondines.AsNoTracking().Include(x => x.Ruta).SingleOrDefaultAsync(x => x.Id == rondinId)
            ?? throw new ReglaNegocioException("El rondín no existe.");
        if (Session.Role != Roles.Administrador && (Session.Role != Roles.Prefecto || Session.UserId != round.PrefectoId))
            throw new ReglaNegocioException("No puedes consultar este rondín.");
        List<CroquisPunto> points;
        if (round.Estado == RondinEstado.Programado)
            points = await db.PuntosControl.AsNoTracking().Where(x => x.RutaId == round.RutaId && x.Activo).OrderBy(x => x.Orden)
                .Select(x => new CroquisPunto(x.Id, x.Orden, x.Nombre, null, null, RegistroResultado.Pendiente, null, null)).ToListAsync();
        else
            points = await db.RegistrosPunto.AsNoTracking().Where(x => x.RondinId == rondinId).OrderBy(x => x.Orden)
                .Select(x => new CroquisPunto(x.Id, x.Orden, x.PuntoNombre, null, null, x.Resultado, x.Observaciones, null)).ToListAsync();
        var ids = points.Select(x => x.Id).ToArray();
        var locations = await db.UbicacionesCroquis.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        points = points.Select(x => x with { X = locations.GetValueOrDefault(x.Id)?.X, Y = locations.GetValueOrDefault(x.Id)?.Y }).ToList();
        return new CroquisRecorrido(round.Ruta.Nombre, round.Estado,
            Session.Role == Roles.Prefecto && Session.UserId == round.PrefectoId && round.Estado == RondinEstado.EnCurso, points);
    }
}
