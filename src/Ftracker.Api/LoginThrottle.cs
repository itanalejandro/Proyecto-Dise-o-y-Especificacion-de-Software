using System.Collections.Concurrent;

namespace Ftracker.Api;

/// <summary>
/// Frena los intentos repetidos de contraseña: tras varios fallos seguidos de una cuenta (o muchos desde una misma PC)
/// se bloquea un rato. Limitación conocida: alguien podría bloquear a propósito una cuenta ajena unos minutos.
/// </summary>
public sealed class LoginThrottle
{
    private sealed class Bucket { public int Failures; public DateTime WindowStart; public DateTime? LockedUntil; }

    private readonly ConcurrentDictionary<string, Bucket> buckets = new();
    private readonly int maxPerAccount, maxPerIp;
    private readonly TimeSpan window, lockout;
    private readonly Func<DateTime> clock;

    public LoginThrottle(int maxPerAccount = 5, int maxPerIp = 30, TimeSpan? window = null, TimeSpan? lockout = null, Func<DateTime>? clock = null)
    {
        this.maxPerAccount = maxPerAccount; this.maxPerIp = maxPerIp;
        this.window = window ?? TimeSpan.FromMinutes(15); this.lockout = lockout ?? TimeSpan.FromMinutes(5);
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public bool IsLocked(string account, string ip, out TimeSpan wait)
    {
        var now = clock(); wait = TimeSpan.Zero;
        foreach (var key in Keys(account, ip))
            if (buckets.TryGetValue(key, out var bucket))
                lock (bucket)
                    if (bucket.LockedUntil is { } until && until > now) wait = wait > until - now ? wait : until - now;
        return wait > TimeSpan.Zero;
    }

    /// <summary>Registra un fallo. Devuelve true si este fallo hizo que la cuenta quedara bloqueada.</summary>
    public bool RegisterFailure(string account, string ip)
    {
        var now = clock(); var accountLocked = false;
        foreach (var key in Keys(account, ip))
        {
            var bucket = buckets.GetOrAdd(key, _ => new Bucket { WindowStart = now });
            lock (bucket)
            {
                if (bucket.LockedUntil is { } until && until <= now) { bucket.LockedUntil = null; bucket.Failures = 0; bucket.WindowStart = now; }
                if (now - bucket.WindowStart > window) { bucket.Failures = 0; bucket.WindowStart = now; }
                bucket.Failures++;
                var limit = key.StartsWith("a:", StringComparison.Ordinal) ? maxPerAccount : maxPerIp;
                if (bucket.Failures >= limit && bucket.LockedUntil is null)
                {
                    bucket.LockedUntil = now + lockout;
                    if (key.StartsWith("a:", StringComparison.Ordinal)) accountLocked = true;
                }
            }
        }
        return accountLocked;
    }

    public void Reset(string account) => buckets.TryRemove("a:" + account, out _);

    private static string[] Keys(string account, string ip) => ["a:" + account, "i:" + ip];
}
