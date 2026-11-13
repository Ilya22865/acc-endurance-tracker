using EnduranceHub.Server.Hubs;
using EnduranceHub.Server.Services;
using EnduranceHub.Domain;
using EnduranceHub.Server.Controllers;
using EnduranceHub.Server.Diagnostics;
using EnduranceHub.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Переносим настройки/контуры из старой папки AccEndurance (один раз).
AppPaths.MigrateLegacyData();

// Пользовательские настройки сервера хранятся вне папки установки: Velopack
// целиком заменяет current при обновлении, поэтому appsettings.json рядом с exe
// не подходит для правок пользователя.
var userDataDir = AppPaths.LocalDataDirectory;
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

var logDir = Path.Combine(AppPaths.LocalDataDirectory, "logs");
try
{
    Directory.CreateDirectory(logDir);
}
catch
{
    logDir = Path.Combine(Path.GetTempPath(), "EnduranceHub-logs");
}
builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(logDir, "server.log")));
// EF Core пишет каждый SQL-запрос на уровне Information. Встроенный сервер читает
// stdout построчно, и этот поток забивал консоль/вкладку «Сервер» до зависания.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// В старом server.appsettings.json ключ назывался "AccEndurance", а база — acc_endurance.
// Поддерживаем оба варианта и один раз переименовываем базу, чтобы не потерять данные.
var configuredConnectionString =
    builder.Configuration.GetConnectionString("EnduranceHub")
    ?? builder.Configuration.GetConnectionString("AccEndurance");
var (connectionString, _) = MigrateDatabase(configuredConnectionString);

// Заодно переписываем старые имена в пользовательском конфиге. Если база уже
// переименована, останется только ключ/DisplayName.
try
{
    if (File.Exists(userSettingsPath))
    {
        var text = File.ReadAllText(userSettingsPath);
        var updated = text
            .Replace("\"AccEndurance\":", "\"EnduranceHub\":")
            .Replace("Database=acc_endurance", "Database=endurancehub")
            .Replace("AccEnduranceServer", "EnduranceHubServer");
        if (updated != text) File.WriteAllText(userSettingsPath, updated);
    }
}
catch
{
    // Не критично: сервер уже использует правильную строку подключения в памяти.
}

builder.Services.AddDbContextFactory<EnduranceHubDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<SessionStateService>();
builder.Services.AddHostedService<ParentProcessWatchdog>();
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
    var db = scope.ServiceProvider.GetRequiredService<EnduranceHubDbContext>();
    await db.Database.MigrateAsync();

    // После перезапуска сервера память пуста: поднимаем историю последнего события,
    // чтобы стинты были видны и их можно было редактировать.
    await scope.ServiceProvider.GetRequiredService<SessionStateService>()
        .LoadLatestStintHistoryAsync();
}

app.Run();

static (string ConnectionString, bool Renamed) MigrateDatabase(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        return (connectionString ?? "", false);

    try
    {
        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        const string target = "endurancehub";
        const string legacy = "acc_endurance";

        var requested = connectionBuilder.Database;
        if (string.IsNullOrWhiteSpace(requested))
            return (connectionString, false);

        // Пользовательские имена баз не трогаем: миграция касается только старой базы проекта.
        var isLegacyRequest = string.Equals(requested, legacy, StringComparison.OrdinalIgnoreCase);
        var isTargetRequest = string.Equals(requested, target, StringComparison.OrdinalIgnoreCase);
        if (!isLegacyRequest && !isTargetRequest)
            return (connectionString, false);

        var adminBuilder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
        connection.Open();

        var legacyExists = DatabaseExists(connection, legacy);
        var targetExists = DatabaseExists(connection, target);

        // Если новая база уже есть, но пустая (её мог успеть создать EF до
        // переименования), а старая содержит данные — сохраняем именно данные.
        if (legacyExists && targetExists &&
            IsDatabaseEmpty(adminBuilder.ConnectionString, target) &&
            !IsDatabaseEmpty(adminBuilder.ConnectionString, legacy))
        {
            TerminateConnections(connection, target);
            ExecuteNonQuery(connection, $"DROP DATABASE \"{target}\"");
            // В пуле могли остаться соединения к удалённой базе — иначе EF
            // получит уже закрытый сокет и упадёт при старте.
            NpgsqlConnection.ClearAllPools();
            targetExists = false;
            Console.WriteLine($"Dropped empty database: {target}");
        }

        if (legacyExists && !targetExists)
        {
            TerminateConnections(connection, legacy);
            ExecuteNonQuery(connection, $"ALTER DATABASE \"{legacy}\" RENAME TO \"{target}\"");
            NpgsqlConnection.ClearAllPools();
            targetExists = true;
            Console.WriteLine($"Database renamed: {legacy} -> {target}");
        }

        if (targetExists)
        {
            connectionBuilder.Database = target;
            return (connectionBuilder.ConnectionString, true);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Database migration skipped: " + ex.Message);
    }

    return (connectionString, false);
}

static bool IsDatabaseEmpty(string adminConnectionString, string database)
{
    var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
    {
        Database = database,
        Pooling = false
    };
    using var connection = new NpgsqlConnection(builder.ConnectionString);
    connection.Open();
    using var command = new NpgsqlCommand(
        "SELECT CASE WHEN to_regclass('public.\"Stints\"') IS NULL THEN 0 " +
        "ELSE (SELECT count(*)::bigint FROM \"Stints\") END", connection);
    return Convert.ToInt64(command.ExecuteScalar()) == 0;
}

static void TerminateConnections(NpgsqlConnection connection, string database)
{
    using var command = new NpgsqlCommand(
        "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
        "WHERE datname = @name AND pid <> pg_backend_pid()", connection);
    command.Parameters.AddWithValue("name", database);
    command.ExecuteNonQuery();
}

static void ExecuteNonQuery(NpgsqlConnection connection, string sql)
{
    using var command = new NpgsqlCommand(sql, connection);
    command.ExecuteNonQuery();
}

static bool DatabaseExists(NpgsqlConnection connection, string name)
{
    using var command = new NpgsqlCommand(
        "SELECT 1 FROM pg_database WHERE datname = @name", connection);
    command.Parameters.AddWithValue("name", name);
    return command.ExecuteScalar() != null;
}