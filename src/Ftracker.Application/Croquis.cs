using Ftracker.Domain;

namespace Ftracker.Application;

public sealed record CroquisPunto(Guid Id, int Orden, string Nombre, double? X, double? Y,
    RegistroResultado Resultado = RegistroResultado.Pendiente, string? Observaciones = null, byte[]? Version = null);
public sealed record CroquisRecorrido(string Nombre, RondinEstado Estado, bool PuedeRegistrar, IReadOnlyList<CroquisPunto> Puntos);
