namespace Ftracker.Domain;

public enum RondinEstado { Programado, EnCurso, Cerrado, Cancelado }
public enum RegistroResultado { Pendiente, Completado, Omitido }
public enum IncidenciaEstado { Abierta, EnRevision, Resuelta, Cerrada }
public enum IncidenciaPrioridad { Baja, Media, Alta, Critica }

public abstract class EntidadAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public string? CreadoPorId { get; set; }
    public DateTime? ActualizadoEn { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class Ruta : EntidadAuditable
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool Activa { get; set; } = true;
    public ICollection<PuntoControl> Puntos { get; set; } = new List<PuntoControl>();
}

public sealed class PuntoControl : EntidadAuditable
{
    public Guid RutaId { get; set; }
    public Ruta Ruta { get; set; } = null!;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class Rondin : EntidadAuditable
{
    public Guid RutaId { get; set; }
    public Ruta Ruta { get; set; } = null!;
    public string PrefectoId { get; set; } = string.Empty;
    public DateTime ProgramadoPara { get; set; }
    public DateTime? IniciadoEn { get; set; }
    public DateTime? CerradoEn { get; set; }
    public RondinEstado Estado { get; set; } = RondinEstado.Programado;
    public string? Observaciones { get; set; }
    public ICollection<RegistroPunto> Registros { get; set; } = new List<RegistroPunto>();
    public ICollection<Incidencia> Incidencias { get; set; } = new List<Incidencia>();
}

public sealed class RegistroPunto : EntidadAuditable
{
    public Guid RondinId { get; set; }
    public Rondin Rondin { get; set; } = null!;
    public Guid PuntoControlId { get; set; }
    public string PuntoNombre { get; set; } = string.Empty;
    public int Orden { get; set; }
    public DateTime? MarcadoEn { get; set; }
    public RegistroResultado Resultado { get; set; } = RegistroResultado.Pendiente;
    public string? Observaciones { get; set; }
}

public sealed class Incidencia : EntidadAuditable
{
    public Guid RondinId { get; set; }
    public Rondin Rondin { get; set; } = null!;
    public Guid? PuntoControlId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public IncidenciaPrioridad Prioridad { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public IncidenciaEstado Estado { get; set; } = IncidenciaEstado.Abierta;
    public DateTime? ResueltaEn { get; set; }
    public ICollection<Evidencia> Evidencias { get; set; } = new List<Evidencia>();
}

public sealed class Evidencia : EntidadAuditable
{
    public Guid IncidenciaId { get; set; }
    public Incidencia Incidencia { get; set; } = null!;
    public string NombreOriginal { get; set; } = string.Empty;
    public string RutaArchivo { get; set; } = string.Empty;
    public string TipoMime { get; set; } = string.Empty;
    public DateTime SubidaEn { get; set; } = DateTime.UtcNow;
}

public sealed class Auditoria
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActorId { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string Entidad { get; set; } = string.Empty;
    public string EntidadId { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public string? Detalle { get; set; }
}
