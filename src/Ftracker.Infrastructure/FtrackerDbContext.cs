using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public sealed class ApplicationUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public sealed class FtrackerDbContext(DbContextOptions<FtrackerDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<PuntoControl> PuntosControl => Set<PuntoControl>();
    public DbSet<Rondin> Rondines => Set<Rondin>();
    public DbSet<RegistroPunto> RegistrosPunto => Set<RegistroPunto>();
    public DbSet<Incidencia> Incidencias => Set<Incidencia>();
    public DbSet<Evidencia> Evidencias => Set<Evidencia>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<UbicacionCroquis> UbicacionesCroquis => Set<UbicacionCroquis>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<UbicacionCroquis>().ToTable("UbicacionesCroquis", t =>
            t.HasCheckConstraint("CK_UbicacionesCroquis_Rango", "X >= 0 AND X <= 1 AND Y >= 0 AND Y <= 1"));
        b.Entity<ApplicationUser>(e => { e.HasIndex(x => x.Email).IsUnique(); e.Property(x => x.Email).HasMaxLength(254); });
        b.Entity<Ruta>(e => { e.Property(x => x.Nombre).HasMaxLength(120).IsRequired(); });
        b.Entity<PuntoControl>(e => { e.Property(x => x.Nombre).HasMaxLength(120).IsRequired(); e.HasIndex(x => new { x.RutaId, x.Orden }).IsUnique(); e.HasOne(x => x.Ruta).WithMany(x => x.Puntos).HasForeignKey(x => x.RutaId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<Rondin>(e => { e.HasIndex(x => new { x.PrefectoId, x.Estado, x.ProgramadoPara }); e.HasOne(x => x.Ruta).WithMany().HasForeignKey(x => x.RutaId).OnDelete(DeleteBehavior.Restrict); e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PrefectoId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<RegistroPunto>(e => { e.HasIndex(x => new { x.RondinId, x.PuntoControlId }).IsUnique(); e.HasOne(x => x.Rondin).WithMany(x => x.Registros).HasForeignKey(x => x.RondinId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<Incidencia>(e => { e.Property(x => x.Tipo).HasMaxLength(80).IsRequired(); e.HasIndex(x => new { x.Estado, x.Prioridad, x.CreadoEn }); e.HasOne(x => x.Rondin).WithMany(x => x.Incidencias).HasForeignKey(x => x.RondinId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<Evidencia>(e => { e.HasOne(x => x.Incidencia).WithMany(x => x.Evidencias).HasForeignKey(x => x.IncidenciaId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<Auditoria>(e => { e.Property(x => x.Accion).HasMaxLength(80); e.HasIndex(x => new { x.Entidad, x.EntidadId, x.Fecha }); });
        foreach (var type in b.Model.GetEntityTypes().Where(x => typeof(EntidadAuditable).IsAssignableFrom(x.ClrType)))
            b.Entity(type.ClrType).Property(nameof(EntidadAuditable.RowVersion)).IsConcurrencyToken();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<EntidadAuditable>().Where(x => x.State == EntityState.Added || x.State == EntityState.Modified))
            entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
        return base.SaveChangesAsync(cancellationToken);
    }
}
