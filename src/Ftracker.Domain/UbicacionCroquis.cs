namespace Ftracker.Domain;

// Id is a PuntoControl ID (catalog) or a RegistroPunto ID (immutable location snapshot).
// Separate storage allows upgrading the original beta without altering existing tables.
public sealed class UbicacionCroquis
{
    public Guid Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}
