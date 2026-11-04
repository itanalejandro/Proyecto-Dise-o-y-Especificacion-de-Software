using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Ftracker.Api;

/// <summary>
/// Sesiones de la API: un token aleatorio opaco por inicio de sesión. Solo se guarda su hash en memoria
/// (reiniciar el servidor cierra todas las sesiones). Caduca por inactividad y por antigüedad máxima.
/// </summary>
public sealed class TokenStore
{
    private sealed record Entry(string UserId, DateTime Created, DateTime LastSeen);

    private readonly ConcurrentDictionary<string, Entry> entries = new();
    private readonly TimeSpan idle, absolute;
    private readonly Func<DateTime> clock;
    private int operations;

    public TokenStore(TimeSpan idle, TimeSpan absolute, Func<DateTime>? clock = null)
    {
        this.idle = idle; this.absolute = absolute; this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public string Issue(string userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock();
        entries[Hash(token)] = new Entry(userId, now, now);
        if (Interlocked.Increment(ref operations) % 50 == 0) Purge(now);
        return token;
    }

    /// <summary>Devuelve el usuario dueño del token y renueva su inactividad; null si no existe o caducó.</summary>
    public string? Touch(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
        var key = Hash(token);
        if (!entries.TryGetValue(key, out var entry)) return null;
        var now = clock();
        if (now - entry.LastSeen > idle || now - entry.Created > absolute) { entries.TryRemove(key, out _); return null; }
        entries[key] = entry with { LastSeen = now };
        return entry.UserId;
    }

    public void Revoke(string token) => entries.TryRemove(Hash(token), out _);
    public void RevokeUser(string userId) { foreach (var pair in entries.Where(x => x.Value.UserId == userId).ToList()) entries.TryRemove(pair.Key, out _); }
    public void Clear() => entries.Clear();
    public int Count => entries.Count;

    private void Purge(DateTime now) { foreach (var pair in entries.Where(x => now - x.Value.LastSeen > idle || now - x.Value.Created > absolute).ToList()) entries.TryRemove(pair.Key, out _); }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
