using System.Security.Cryptography;
using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public sealed class LocalSession : ISession
{
    public ApplicationUser? User { get; private set; }
    public string? UserId => User?.Id;
    public string? Role => User?.Rol;
    internal void SignIn(ApplicationUser user) => User = user;
    public void SignOut() => User = null;
}

public sealed class LocalStore
{
    private readonly DbContextOptions<FtrackerDbContext> options;
    public string DataDirectory { get; }
    public LocalSession Session { get; } = new();
    public LocalStore(string? directory = null)
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
        DataDirectory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ftracker");
        Directory.CreateDirectory(DataDirectory);
        options = new DbContextOptionsBuilder<FtrackerDbContext>().UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(DataDirectory, "ftracker-desktop.db"), ForeignKeys = true }.ToString()).Options;
    }
    public FtrackerDbContext Open() => new(options);
    public async Task InitializeAsync() { await using var db = Open(); await db.Database.EnsureCreatedAsync(); }
    public async Task<bool> NeedsSetupAsync() { await using var db = Open(); return !await db.Users.AnyAsync(); }
    public async Task SetupAsync(string name, string email, string password)
    {
        await using var db = Open();
        if (await db.Users.AnyAsync()) throw new ReglaNegocioException("La configuración inicial ya se completó.");
        var admin = CreateUser(name, email, password, Roles.Administrador);
        db.Users.Add(admin); await db.SaveChangesAsync();
    }
    public async Task LoginAsync(string email, string password)
    {
        Session.SignOut();
        await using var db = Open();
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == normalized);
        if (user is null || !user.Activo || !Verify(password, user.PasswordHash)) throw new ReglaNegocioException("Correo o contraseña incorrectos, o cuenta desactivada.");
        Session.SignIn(user);
    }
    public async Task AddUserAsync(string name, string email, string password, string role)
    {
        RequireAdmin();
        var user = CreateUser(name, email, password, role);
        await using var db = Open();
        if (await db.Users.AnyAsync(x => x.Email == user.Email)) throw new ReglaNegocioException("El correo ya está registrado.");
        db.Users.Add(user); Audit(db, "CrearUsuario", user.Id); await db.SaveChangesAsync();
    }
    public async Task AddRouteAsync(string name, string description, string points)
    {
        RequireAdmin();
        var names = points.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || names.Length == 0 || names.Any(x => x.Length > 120)) throw new ReglaNegocioException("Escribe un nombre de ruta y al menos un punto (máximo 120 caracteres por nombre).");
        await using var db = Open();
        var route = new Ruta { Nombre = name.Trim(), Descripcion = description.Trim(), CreadoPorId = Session.UserId };
        for (var i = 0; i < names.Length; i++) route.Puntos.Add(new PuntoControl { Nombre = names[i], Orden = i + 1, CreadoPorId = Session.UserId });
        db.Rutas.Add(route); Audit(db, "CrearRuta", route.Id.ToString()); await db.SaveChangesAsync();
    }
    public async Task ToggleUserAsync(string id)
    {
        RequireAdmin(); if (id == Session.UserId) throw new ReglaNegocioException("No puedes desactivar tu propia cuenta.");
        await using var db = Open(); var user = await db.Users.SingleAsync(x => x.Id == id);
        user.Activo = !user.Activo; Audit(db, "CambiarUsuarioActivo", id); await db.SaveChangesAsync();
    }
    public async Task ToggleRouteAsync(Guid id)
    {
        RequireAdmin(); await using var db = Open(); var route = await db.Rutas.SingleAsync(x => x.Id == id);
        route.Activa = !route.Activa; Audit(db, "CambiarRutaActiva", id.ToString()); await db.SaveChangesAsync();
    }
    public async Task SaveEvidenceAsync(Guid incidentId, string path)
    {
        await using var db = Open();
        var incident = await db.Incidencias.Include(x => x.Rondin).Include(x => x.Evidencias).SingleAsync(x => x.Id == incidentId);
        if (Session.UserId != incident.Rondin.PrefectoId || incident.Rondin.Estado != RondinEstado.EnCurso) throw new ReglaNegocioException("No puedes adjuntar evidencia a esta incidencia.");
        var file = new FileInfo(path);
        var ext = file.Extension.ToLowerInvariant();
        if (!new[] { ".jpg", ".jpeg", ".png", ".pdf" }.Contains(ext) || !file.Exists || file.Length == 0 || file.Length > 5 * 1024 * 1024 || incident.Evidencias.Count >= 5) throw new ReglaNegocioException("Admite JPG, PNG o PDF hasta 5 MB; máximo 5 archivos por incidencia.");
        var header = new byte[8]; using (var stream = file.OpenRead()) stream.ReadExactly(header.AsSpan(0, (int)Math.Min(8, file.Length)));
        var valid = ext switch { ".pdf" => header[0] == 37 && header[1] == 80 && header[2] == 68 && header[3] == 70, ".png" => header.AsSpan().SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}), _ => header[0] == 255 && header[1] == 216 && header[2] == 255 };
        if (!valid) throw new ReglaNegocioException("El contenido no coincide con el tipo de archivo.");
        var directory = Path.Combine(DataDirectory, "evidencias"); Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Guid.NewGuid() + ext);
        File.Copy(path, destination);
        try
        {
            db.Evidencias.Add(new Evidencia { IncidenciaId = incidentId, NombreOriginal = file.Name, RutaArchivo = destination, TipoMime = ext == ".pdf" ? "application/pdf" : ext == ".png" ? "image/png" : "image/jpeg", CreadoPorId = Session.UserId });
            Audit(db, "AdjuntarEvidencia", incidentId.ToString()); await db.SaveChangesAsync();
        }
        catch { File.Delete(destination); throw; }
    }
    public void RequireAdmin() { if (Session.Role != Roles.Administrador) throw new ReglaNegocioException("Se requiere administrador."); }
    private void Audit(FtrackerDbContext db, string action, string id) => db.Auditorias.Add(new Auditoria { ActorId = Session.UserId!, Accion = action, Entidad = "Catalogo", EntidadId = id });
    private static ApplicationUser CreateUser(string name, string email, string password, string role)
    {
        if (string.IsNullOrWhiteSpace(name) || !System.Net.Mail.MailAddress.TryCreate(email.Trim(), out _) || password.Length < 12 || (role != Roles.Administrador && role != Roles.Prefecto)) throw new ReglaNegocioException("Escribe nombre, correo válido, rol y una contraseña de al menos 12 caracteres.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
        return new ApplicationUser { NombreCompleto = name.Trim(), Email = email.Trim().ToLowerInvariant(), Rol = role, PasswordHash = $"210000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}" };
    }
    private static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[1]), int.Parse(parts[0]), HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(parts[2]));
    }
}
