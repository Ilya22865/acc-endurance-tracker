using AccEndurance.Agent;

var options = ParseArgs(args);

Console.WriteLine("ACC Endurance Agent v1.0");
Console.WriteLine($"  Сервер:   {options.ServerUrl}");
Console.WriteLine($"  Agent ID: {options.AgentId}");

var batch = new List<TelemetryReading>(1000);
var driverIndex = -1;
var driverName = "";
var driverId = ""; // уникальный ID для этого пилота
var lastFlush = DateTime.UtcNow;
var samplesSent = 0L;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var client = new ServerClient(options.ServerUrl, options.AgentId);

driverIndex = options.DriverIndex;

if (driverIndex < 0)
{
    List<ServerClient.TeamDriverDto> teamDrivers;
    try
    {
        teamDrivers = await client.GetTeamDriversAsync(cts.Token);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        Console.WriteLine($"Не удалось получить список пилотов с {options.ServerUrl}: {ex.Message}");
        Console.WriteLine("Проверьте, что сервер запущен, или укажите --driver-index N.");
        return;
    }

    if (teamDrivers.Count == 0)
    {
        Console.WriteLine("Нет списка пилотов (нет данных о команде). Укажите --driver-index N.");
        return;
    }

    Console.WriteLine("\nВыберите пилота:");
    for (int i = 0; i < teamDrivers.Count; i++)
        Console.WriteLine($"  {i + 1}. {teamDrivers[i]}");

    Console.Write("> ");
    var line = Console.ReadLine();
    if (!int.TryParse(line, out var choice) || choice < 1 || choice > teamDrivers.Count)
    {
        Console.WriteLine("Некорректный выбор, выходим.");
        return;
    }

    driverIndex = teamDrivers[choice - 1].DriverIndex;
    driverName = teamDrivers[choice - 1].DisplayName;
}

driverId = $"{options.AgentId}:{driverIndex}";

using IAccDataSource source = options.UseFakeDataSource
    ? new FakeAccDataSource()
    : new AccSharedMemoryReader();
source.Start();

Console.Write($"Регистрирую пилота (DriverIndex={driverIndex})... ");
var registered = await client.RegisterAsync(driverIndex, driverName, cts.Token);
Console.WriteLine(registered ? "OK" : "FAILED");

Console.WriteLine("Агент работает. Ctrl+C для выхода.\n");

var lastRegister = DateTime.UtcNow;
var lastPacketId = int.MinValue;

try
{
    while (!cts.IsCancellationRequested)
    {
        // читаем часто (≈40 Гц) и пропускаем повторы: графики в клиенте строятся в реальном времени
        if (source.TryReadSample(out var sample) && sample.PacketId != lastPacketId)
        {
            lastPacketId = sample.PacketId;
            batch.Add(sample);
            samplesSent++;
        }

        // Регистрация хранится на сервере в памяти и пропадает при его перезапуске
        // (после этого батчи молча отбрасывались). Повторяем её периодически — запрос идемпотентный.
        if (DateTime.UtcNow - lastRegister >= TimeSpan.FromSeconds(30))
        {
            lastRegister = DateTime.UtcNow;
            await client.RegisterAsync(driverIndex, driverName, cts.Token);
        }

        if (batch.Count >= 500 || DateTime.UtcNow - lastFlush >= TimeSpan.FromMilliseconds(options.SendIntervalMs))
        {
            var sent = await client.SendBatchAsync(batch, driverId, cts.Token);
            batch.Clear();
            lastFlush = DateTime.UtcNow;

            // сервер ответил отказом (обычно 409 — потерял регистрацию после перезапуска): регистрируемся сразу
            if (!sent && DateTime.UtcNow - lastRegister >= TimeSpan.FromSeconds(2))
            {
                lastRegister = DateTime.UtcNow;
                await client.RegisterAsync(driverIndex, driverName, cts.Token);
            }
        }

        await Task.Delay(options.ReadIntervalMs, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C — штатный выход (раньше TaskCanceledException из Task.Delay вылетала необработанной)
}

Console.WriteLine($"Отправлено сэмплов: {samplesSent}");
Console.WriteLine("Агент остановлен.");

static AgentOptions ParseArgs(string[] args)
{
    var options = new AgentOptions();
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--server": options.ServerUrl = args[++i]; break;
            case "--agent-id": options.AgentId = args[++i]; break;
            case "--driver-index": options.DriverIndex = int.Parse(args[++i]); break;
            case "--read-interval":
            case "--interval-ms": options.ReadIntervalMs = int.Parse(args[++i]); break;
            case "--send-interval-ms": options.SendIntervalMs = int.Parse(args[++i]); break;
            case "--fake": options.UseFakeDataSource = true; break;
        }
    }
    return options;
}

sealed class AgentOptions
{
    public string ServerUrl { get; set; } = "http://127.0.0.1:5261";
    public string AgentId { get; set; } = Environment.MachineName;
    public int DriverIndex { get; set; } = -1;
    public int ReadIntervalMs { get; set; } = 25;
    public int SendIntervalMs { get; set; } = 250;
    public bool UseFakeDataSource { get; set; } = false;
}