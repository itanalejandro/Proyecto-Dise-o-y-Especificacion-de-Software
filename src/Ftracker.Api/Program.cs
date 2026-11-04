using Ftracker.Api;
using Ftracker.Application;
using Ftracker.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "Ftracker API");

var dataDirectory = builder.Configuration["Ftracker:DataDirectory"];

// Google Drive (opcional): evidencias y respaldo diario. Sin llave de cuenta de servicio, todo se guarda en el disco del servidor.
GoogleDriveClient? drive = null; IEvidenceBlobStore? blobs = null;
var keyFile = builder.Configuration["Drive:ServiceAccountKeyFile"];
if (!string.IsNullOrWhiteSpace(keyFile))
{
    drive = new GoogleDriveClient(GoogleDriveClient.LoadServiceAccountKey(keyFile));
    var evidenceFolder = builder.Configuration["Drive:EvidenceFolderId"];
    if (!string.IsNullOrWhiteSpace(evidenceFolder)) blobs = new DriveEvidenceStore(drive, evidenceFolder);
}

var factory = new LocalStoreFactory(string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory, blobs);
await factory.InitializeAsync();

builder.Services.AddSingleton<IStoreFactory>(factory);
builder.Services.AddSingleton(new TokenStore(
    TimeSpan.FromMinutes(builder.Configuration.GetValue("Ftracker:TokenIdleMinutes", 60)),
    TimeSpan.FromHours(builder.Configuration.GetValue("Ftracker:TokenMaxHours", 12))));
builder.Services.AddSingleton(new LoginThrottle());
builder.Services.ConfigureHttpJsonOptions(o => ApiJson.Configure(o.SerializerOptions));
builder.WebHost.ConfigureKestrel(k => { k.Limits.MaxRequestBodySize = 1_000_000; k.AddServerHeader = false; });

var backupFolder = builder.Configuration["Drive:BackupFolderId"];
if (drive is not null && !string.IsNullOrWhiteSpace(backupFolder))
    builder.Services.AddHostedService(services => new DailyBackupService(factory, drive, backupFolder,
        TimeOnly.Parse(builder.Configuration["Drive:BackupTime"] ?? "02:00", System.Globalization.CultureInfo.InvariantCulture),
        builder.Configuration.GetValue("Drive:BackupsToKeep", 14), services.GetRequiredService<ILogger<DailyBackupService>>()));

var app = builder.Build();
ApiEndpoints.Map(app);
app.Run();
