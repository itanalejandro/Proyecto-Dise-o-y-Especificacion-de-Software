using System.Buffers.Binary;
using System.Security.Cryptography;
using Ftracker.Application;
using Ftracker.Domain;

namespace Ftracker.Infrastructure;

internal static class ClosurePhoto
{
    internal const int MaxBytes = 5 * 1024 * 1024;

    internal static EvidenciaCierre Create(FotoCierreCommand? command)
    {
        if (command is null) throw new ReglaNegocioException("Adjunta una fotografía de la hoja de firmas para cerrar el rondín.");
        var name = Path.GetFileName((command.NombreArchivo ?? "").Replace('\\', '/'));
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || extension is not (".jpg" or ".jpeg" or ".png") ||
            command.Contenido is not { Length: > 0 and <= MaxBytes })
            throw new ReglaNegocioException("La hoja de firmas debe ser una fotografía JPG o PNG de hasta 5 MB.");
        var bytes = command.Contenido.ToArray(); // Snapshot: the caller cannot change the original after closing.
        var size = Dimensions(bytes, extension);
        if (size.Width <= 0 || size.Height <= 0 || size.Width > 20000 || size.Height > 20000 || (long)size.Width * size.Height > 80000000)
            throw new ReglaNegocioException("La fotografía está dañada o tiene dimensiones no compatibles. Elige otra imagen.");
        return new EvidenciaCierre { NombreOriginal = name, TipoMime = extension == ".png" ? "image/png" : "image/jpeg",
            Contenido = bytes, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
    }

    // Structural checks, not recognition of signatures or proof of physical presence.
    private static (int Width, int Height) Dimensions(byte[] b, string extension)
    {
        if (extension == ".png")
        {
            if (b.Length < 57 || !b.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) return default;
            var position = 8; var header = false; var data = false; var width = 0; var height = 0;
            while (position <= b.Length - 12)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(position, 4));
                if (length > (uint)(b.Length - position - 12)) return default;
                var type = System.Text.Encoding.ASCII.GetString(b, position + 4, 4);
                if (!header)
                {
                    if (type != "IHDR" || length != 13) return default;
                    width = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(position + 8, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(position + 12, 4)); header = true;
                }
                else if (type == "IHDR") return default;
                if (type == "IDAT" && length > 0) data = true;
                position += checked((int)length + 12);
                if (type == "IEND") return length == 0 && position == b.Length && data ? (width, height) : default;
            }
            return default;
        }
        if (b.Length < 16 || b[0] != 255 || b[1] != 216 || b[^2] != 255 || b[^1] != 217) return default;
        var offset = 2; var dimensions = (Width: 0, Height: 0);
        while (offset < b.Length - 4)
        {
            if (b[offset++] != 255) return default;
            while (offset < b.Length && b[offset] == 255) offset++;
            if (offset >= b.Length - 2) return default;
            var marker = b[offset++];
            if (marker == 0xDA) return dimensions; // SOS: decoder in the desktop validates the preview.
            if (marker is 0x00 or 0xD8 or 0xD9) return default;
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;
            var length = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(offset, 2));
            if (length < 2 || offset + length > b.Length) return default;
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 8) return default;
                dimensions = (BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(offset + 5, 2)), BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(offset + 3, 2)));
            }
            offset += length;
        }
        return default;
    }
}
