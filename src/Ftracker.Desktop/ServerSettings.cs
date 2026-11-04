using System.IO;
using System.Text.Json;
using Ftracker.Application;

namespace Ftracker.Desktop;

/// <summary>
/// Dirección del servidor de prefectura, guardada en %LOCALAPPDATA%\Ftracker\servidor.json.
/// Si el archivo no existe, la aplicación usa los datos locales de este equipo como hasta ahora.
/// </summary>
internal sealed record ServerSettings(string Url, string? CertificateSha256)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ftracker", "servidor.json");

    public string Host => Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : Url;

    public static ServerSettings? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var settings = JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(FilePath), Json);
            return settings is { Url.Length: > 0 } ? settings : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // No se cae en silencio a los datos locales: el usuario creería estar trabajando contra el servidor.
            throw new ReglaNegocioException("El archivo de configuración del servidor está dañado. Ábrelo desde Acceso → Servidor para corregirlo, o bórralo para usar los datos de este equipo: " + FilePath);
        }
    }

    /// <summary>Guarda la configuración. Una dirección vacía vuelve al modo local.</summary>
    public static void Save(string? url, string? fingerprint)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new ReglaNegocioException("Escribe una dirección que empiece con https:// (por ejemplo https://ftracker-prefectura:8443).");
        var pin = new string((fingerprint ?? "").Where(Uri.IsHexDigit).ToArray());
        if (pin.Length != 0 && pin.Length != 64) throw new ReglaNegocioException("La huella SHA-256 debe tener 64 caracteres hexadecimales. Déjala vacía solo si el certificado lo emite una entidad de confianza.");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new ServerSettings(uri.ToString().TrimEnd('/'), pin.Length == 0 ? null : pin.ToUpperInvariant()), Json));
    }
}
