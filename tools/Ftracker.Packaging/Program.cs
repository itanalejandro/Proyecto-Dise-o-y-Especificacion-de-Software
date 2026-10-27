using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

internal sealed record Options(string Version = "3.0.0-beta.1", bool PortableOnly = false, bool SkipTests = false, string? Iscc = null);

internal static class Program
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly Regex VersionPattern = new(@"^\d+\.\d+\.\d+(-[A-Za-z0-9]+([.-][A-Za-z0-9]+)*)?$", RegexOptions.CultureInvariant);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Any(x => x is "--help" or "-h" or "-?"))
            {
                Console.WriteLine("Ftracker.Packaging: [--version 3.0.0-beta.1] [--portable-only] [--skip-tests] [--iscc ruta] | --self-test");
                Console.WriteLine("Necesita SDK .NET 10; el instalador EXE requiere Inno Setup 6.3 o posterior. No cambia politicas ni certificados.");
                return 0;
            }
            SelfTest();
            if (args.Length == 1 && args[0] == "--self-test") return 0;
            var options = Parse(args);
            await Build(FindRoot(), options);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Falta un valor despues de " + args[i - 1]);
            options = args[i].ToLowerInvariant() switch
            {
                "--version" or "-version" => options with { Version = Value() },
                "--portable-only" or "-portableonly" => options with { PortableOnly = true },
                "--skip-tests" or "-skiptests" => options with { SkipTests = true },
                "--iscc" or "-isccpath" => options with { Iscc = Value() },
                _ => throw new ArgumentException("Opcion desconocida: " + args[i])
            };
        }
        if (!ValidVersion(options.Version)) throw new ArgumentException("Version no valida. Ejemplo: 3.0.0-beta.1 (maximo 40 caracteres). ");
        return options;
    }

    private static bool ValidVersion(string version) => version.Length <= 40 && VersionPattern.IsMatch(version)
        && version.Split('-')[0].Split('.').All(x => ushort.TryParse(x, out _));

    private static string FindRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Ftracker.sln")) && Directory.Exists(Path.Combine(directory.FullName, "src", "Ftracker.Desktop"))) return directory.FullName;
        throw new DirectoryNotFoundException("Ejecuta el empaquetador desde el repositorio de Ftracker.");
    }

    private static string Dotnet() => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)
        ? host : FindExecutable("dotnet.exe", [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")]);

    private static string FindExecutable(string name, IEnumerable<string> candidates)
    {
        foreach (var path in candidates.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(x => Path.Combine(x.Trim('"'), name))))
            if (File.Exists(path)) return Path.GetFullPath(path);
        throw new FileNotFoundException("No se encontro " + name);
    }

    private static string Compiler(string? supplied)
    {
        if (supplied is not null)
            return File.Exists(supplied) ? Path.GetFullPath(supplied) : throw new FileNotFoundException("No existe el compilador indicado: " + supplied);
        var locations = new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Programs", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
        try { return FindExecutable("ISCC.exe", locations.SelectMany(x => new[] { Path.Combine(x, "Inno Setup 6", "ISCC.exe"), Path.Combine(x, "Inno Setup 7", "ISCC.exe") })); }
        catch (FileNotFoundException) { throw new FileNotFoundException("Se necesita el compilador oficial Inno Setup 6.3 o posterior. Usa --portable-only para generar solo el ZIP. No se instala software automaticamente."); }
    }

    private static async Task Run(string executable, string root, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("No se pudo iniciar " + executable);
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        var stderr = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} termino con codigo {process.ExitCode}. Se detuvo la generacion.");
    }

    private static async Task Build(string root, Options options)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La distribucion y sus pruebas WPF se preparan en Windows.");
        var compiler = options.PortableOnly ? null : Compiler(options.Iscc);
        var dotnet = Dotnet();
        var dist = Path.Combine(root, "dist");
        var artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(artifacts);
        using var buildLock = new FileStream(Path.Combine(artifacts, ".packaging.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var baseName = $"Ftracker-{options.Version}-win-x64";
        var zipName = baseName + "-portable.zip";
        var setupName = $"Ftracker-Setup-{options.Version}-win-x64.exe";
        var outputs = options.PortableOnly ? new[] { zipName } : new[] { zipName, setupName };
        foreach (var output in outputs)
            if (File.Exists(Path.Combine(dist, output)) || File.Exists(Path.Combine(dist, output + ".sha256"))) throw new IOException("La entrega ya existe: " + output + ". Conserva esa copia y utiliza otra version.");

        if (options.SkipTests) Console.WriteLine("ADVERTENCIA: --skip-tests omite expresamente las pruebas funcionales y WPF.");
        else
        {
            Console.WriteLine("Ejecutando pruebas funcionales...");
            await Run(dotnet, root, "run", "--project", Path.Combine(root, "tests", "Ftracker.SmokeTests"));
            Console.WriteLine("Ejecutando pruebas WPF...");
            await Run(dotnet, root, "run", "--project", Path.Combine(root, "tests", "Ftracker.DesktopChecks"));
        }
        var stage = Path.Combine(artifacts, "installer-build", Guid.NewGuid().ToString("N")[..8]);
        var portable = Path.Combine(stage, baseName);
        var app = Path.Combine(portable, "app");
        var outputDirectory = Path.Combine(stage, "output");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(outputDirectory);
        Console.WriteLine("Publicando aplicacion autocontenida...");
        await Run(dotnet, root, "publish", Path.Combine(root, "src", "Ftracker.Desktop", "Ftracker.Desktop.csproj"), "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=" + options.Version, "-p:DebugType=None", "-p:DebugSymbols=false", "-o", app);
        ValidateApp(app);
        WritePortableFiles(portable, options.Version);
        WriteManifest(portable);

        if (compiler is not null)
        {
            Console.WriteLine("Compilando instalador con Inno Setup...");
            await Run(compiler, root, "/DSourceDir=" + app, "/DAppVersion=" + options.Version, "/DNumericVersion=" + options.Version.Split('-')[0] + ".0", "/O" + outputDirectory, Path.Combine(root, "installer", "ftracker.iss"));
            if (!File.Exists(Path.Combine(outputDirectory, setupName))) throw new IOException("No se genero el instalador esperado.");
        }
        Console.WriteLine("Comprimiendo y verificando portable...");
        ZipFile.CreateFromDirectory(portable, Path.Combine(outputDirectory, zipName), CompressionLevel.Optimal, includeBaseDirectory: false);
        VerifyZip(Path.Combine(outputDirectory, zipName));
        Directory.CreateDirectory(dist);
        foreach (var output in outputs)
        {
            var path = Path.Combine(dist, output);
            File.Move(Path.Combine(outputDirectory, output), path, overwrite: false);
            File.WriteAllText(path + ".sha256", HashFile(path) + "  " + output + "\r\n", Utf8);
            Console.WriteLine("Entrega generada: " + path);
        }
        Console.WriteLine("Staging conservado: " + stage);
        Console.WriteLine("Sin firma digital. No se modificaron certificados ni protecciones de Windows.");
    }

    private static bool PrivateFile(string name) => Regex.IsMatch(name, @"\.(db(-wal|-shm)?|log|pfx|p12|cer|key|pem)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static void ValidateApp(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "Ftracker.exe"))) throw new IOException("Falta Ftracker.exe en la publicacion.");
        if (Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any(x => PrivateFile(Path.GetFileName(x)))) throw new IOException("La publicacion incluye datos, claves o certificados no permitidos.");
    }

    private static void WritePortableFiles(string directory, string version)
    {
        File.WriteAllText(Path.Combine(directory, "Iniciar-Ftracker.cmd"), "@echo off\r\nstart \"\" \"%~dp0app\\Ftracker.exe\"\r\n", Encoding.ASCII);
        File.WriteAllText(Path.Combine(directory, "VERSION.txt"), version + "\r\n", Utf8);
        File.WriteAllText(Path.Combine(directory, "LEEME.txt"), $"""
            Ftracker {version} - Windows x64 - paquete portable autocontenido

            Extrae TODA esta carpeta antes de abrir Iniciar-Ftracker.cmd o app\Ftracker.exe.
            No necesita SDK ni runtime .NET separado. No ejecutes desde el ZIP.
            No incluye cuentas, claves, base de datos ni evidencias de otra persona.
            Al primer inicio crea tu administrador: no hay cuenta fija de soporte.

            Los datos permanecen en %LOCALAPPDATA%\Ftracker, no dentro del paquete.
            Cambiar de carpeta o de PC no mueve ni sincroniza la base automaticamente.
            Cierra Ftracker y crea un respaldo antes de actualizar. No borres sus datos.

            Este paquete NO tiene firma digital de confianza. No modifica certificados
            ni desactiva protecciones. Si Windows lo bloquea, consulta al responsable.
            El manifiesto SHA-256 comprueba contenido, no certifica al editor.

            Para un acceso directo, utiliza las opciones de Windows sobre Ftracker.exe.
            El instalador EXE, distribuido por separado, ofrece accesos y desinstalacion
            por usuario; conserva la carpeta de datos. No hay actualizacion automatica.
            """, Utf8);
    }

    private static string HashFile(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void WriteManifest(string directory)
    {
        var lines = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(file => HashFile(file) + "  " + Path.GetRelativePath(directory, file).Replace('\\', '/')).ToArray();
        File.WriteAllLines(Path.Combine(directory, "MANIFEST-SHA256.txt"), lines, Utf8);
    }

    private static void VerifyZip(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        var manifest = zip.GetEntry("MANIFEST-SHA256.txt") ?? throw new InvalidDataException("Falta el manifiesto del portable.");
        using var reader = new StreamReader(manifest.Open(), Utf8);
        var entries = zip.Entries.ToDictionary(x => x.FullName, StringComparer.Ordinal);
        var checkedNames = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length < 67 || line.Substring(64, 2) != "  ") throw new InvalidDataException("Manifiesto no valido.");
            var name = line[66..];
            if (!entries.TryGetValue(name, out var entry) || !checkedNames.Add(name)) throw new InvalidDataException("Archivo ausente o repetido en manifiesto.");
            using var bytes = entry.Open();
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != line[..64]) throw new InvalidDataException("Contenido incorrecto en " + name);
        }
        if (checkedNames.Count != entries.Count - 1) throw new InvalidDataException("Hay archivos sin verificar en el ZIP.");
        foreach (var required in new[] { "app/Ftracker.exe", "LEEME.txt", "VERSION.txt", "Iniciar-Ftracker.cmd" })
            if (!checkedNames.Contains(required)) throw new InvalidDataException("El portable esta incompleto: " + required);
        Console.WriteLine("Portable verificado: todos los archivos coinciden con el manifiesto SHA-256.");
    }

    private static void SelfTest()
    {
        static void Check(bool value) { if (!value) throw new InvalidOperationException("Fallo una comprobacion del empaquetador."); }
        Check(ValidVersion("3.0.0-beta.1") && ValidVersion("3.1.0"));
        Check(!ValidVersion("../3.0.0") && !ValidVersion("3.0.0/otro") && !ValidVersion("3.0.0-") && !ValidVersion("999999.0.0"));
        Check(PrivateFile("real.db") && PrivateFile("base.db-wal") && PrivateFile("firma.PFX") && !PrivateFile("Microsoft.Extensions.Logging.dll"));
        Check(Parse(["--version", "3.0.0-beta.2", "--portable-only"]) is { Version: "3.0.0-beta.2", PortableOnly: true });
        Check(Parse(["-Version", "3.1.0", "-SkipTests"]) is { Version: "3.1.0", SkipTests: true });
        try { Parse(["--version"]); throw new InvalidOperationException("Acepto una opcion incompleta."); } catch (ArgumentException) { }
        Console.WriteLine("Comprobaciones internas del empaquetador: correctas.");
    }
}
