using AccEndurance.Server.Hubs;
using AccEndurance.Server.Services;
using AccEndurance.Domain;
using AccEndurance.Server.Controllers;
using AccEndurance.Server.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Пользовательские настройки сервера хранятся вне папки установки: Velopack
// целиком заменяет current при обновлении, поэтому appsettings.json рядом с exe
// не подходит для правок пользователя.
var userDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AccEndurance");
var userSettingsPath = Path.Combine(userDataDir, "server.appsettings.json");
var bundledSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

try
{
    Directory.CreateDirectory(userDataDir);
    if (!File.Exists(userSettingsPath) && File.Exists(bundledSettingsPath))
        File.Copy(bundledSettingsPath, userSettingsPath);
}
catch
{
    // Если создать файл настроек не удалось, сервер продолжит работать
    // с настройками по умолчанию из appsettings.json рядом с exe.
}

builder.Configuration.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: false);

var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccEndurance", "logs");
try
{
    Directory.CreateDirectory(logDir);
}
catch
{
    logDir = Path.Combine(Path.GetTempPath(), "AccEndurance-logs");
}
builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(logDir, "server.log")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddDbContextFactory<AccEnduranceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AccEndurance")));

builder.Services.AddSingleton<SessionStateService>();
builder.Services.AddHostedService<TelemetryPersistService>();
builder.Services.AddSingleton<AccBroadcastService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AccBroadcastService>());
builder.Services.AddSingleton<SessionStateBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionStateBroadcaster>());

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true);
    });
});

var app = builder.Build();

app.UseCors();
app.MapControllers();
app.MapHub<SessionHub>("/hub/session");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccEnduranceDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();