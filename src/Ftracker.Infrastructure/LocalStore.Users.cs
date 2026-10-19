using System.Security.Cryptography;
using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public sealed partial class LocalStore
{
    private const int MinPasswordLength = 12;

    public async Task UpdateUserAsync(string id, string name, string email, string role)
    {
        RequireAdmin();
        name = (name ?? "").Trim(); email = (email ?? "").Trim();
        if (name.Length == 0 || name.Length > 120 || email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out _) || (role != Roles.Administrador && role != Roles.Prefecto))
            throw new ReglaNegocioException("Escribe nombre (máximo 120 caracteres), un correo válido y un rol.");
        await using var db = Open();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id) ?? throw new ReglaNegocioException("El usuario ya no existe. Actualiza los datos.");
        var normalized = email.ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Id != id && x.Email == normalized)) throw new ReglaNegocioException("El correo ya está registrado.");
        if (user.Rol != role)
        {
            if (id == Session.UserId) throw new ReglaNegocioException("No puedes cambiar tu propio rol.");
            if (user.Rol == Roles.Administrador && !await db.Users.AnyAsync(x => x.Id != id && x.Rol == Roles.Administrador && x.Activo))
                throw new ReglaNegocioException("Debe quedar al menos un administrador activo.");
            if (user.Rol == Roles.Prefecto && await db.Rondines.AnyAsync(x => x.PrefectoId == id && (x.Estado == RondinEstado.Programado || x.Estado == RondinEstado.EnCurso)))
                throw new ReglaNegocioException("El usuario tiene rondines programados o en curso. Reasígnalos o cancélalos antes de cambiar su rol.");
        }
        var before = $"{user.NombreCompleto} <{user.Email}> ({user.Rol})";
        user.NombreCompleto = name; user.Email = normalized; user.Rol = role;
        Audit(db, "EditarUsuario", id, $"{before} → {name} <{normalized}> ({role})");
        await db.SaveChangesAsync();
    }

    public async Task ChangeOwnPasswordAsync(string current, string next)
    {
        var id = Session.UserId ?? throw new ReglaNegocioException("Inicia sesión para continuar.");
        ValidateNewPassword(next);
        await using var db = Open();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Activo) ?? throw new ReglaNegocioException("Tu cuenta ya no está disponible.");
        if (!Verify(current ?? "", user.PasswordHash)) throw new ReglaNegocioException("La contraseña actual no es correcta.");
        if (current == next) throw new ReglaNegocioException("La contraseña nueva debe ser distinta de la actual.");
        user.PasswordHash = HashPassword(next);
        Audit(db, "CambiarContrasena", id);
        await db.SaveChangesAsync();
    }

    /// <summary>El administrador fija una contraseña nueva para otro usuario (por ejemplo, si la olvidó).</summary>
    public async Task ResetPasswordAsync(string id, string next)
    {
        RequireAdmin();
        if (id == Session.UserId) throw new ReglaNegocioException("Para cambiar tu propia contraseña usa Cambiar contraseña.");
        ValidateNewPassword(next);
        await using var db = Open();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id) ?? throw new ReglaNegocioException("El usuario ya no existe. Actualiza los datos.");
        user.PasswordHash = HashPassword(next);
        Audit(db, "RestablecerContrasena", id, user.Email);
        await db.SaveChangesAsync();
    }

    private static void ValidateNewPassword(string password)
    {
        if ((password ?? "").Length < MinPasswordLength) throw new ReglaNegocioException($"La contraseña debe tener al menos {MinPasswordLength} caracteres.");
    }

    // Mismo formato que CreateUser: iteraciones.sal.hash
    private static string HashPassword(string password)
    {
        const int iterations = 210000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
}
