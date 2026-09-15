using AccEndurance.Agent;

var options = ParseArgs(args);

Console.WriteLine("ACC Endurance Agent v1.0");
Console.WriteLine($"  Сервер:   {options.ServerUrl}");
Console.WriteLine($"  Agent ID: {options.AgentId}");

var batch = new List<TelemetryReading>(1000);
var driverIndex = -1;
var driverName = "";
var lastFlush = DateTime.UtcNow;
var samplesSent = 0L;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var client = new ServerClient(options.ServerUrl, options.AgentId);

if (options.DriverIndex < 0)
{
    driverIndex = options.DriverIndex;
}
else
{
    driverIndex = options.DriverIndex;
}

if (driverIndex < 0)
{
    var teamDrivers = await client.GetTeamDriversAsync(cts.Token);
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

using IAccDataSource source = options.UseFakeDataSource
    ? new FakeAccDataSource()
    : new AccSharedMemoryReader();
source.Start();

Console.Write($"Регистрирую пилота (DriverIndex={driverIndex})... ");
var registered = await client.RegisterAsync(driverIndex, driverName, cts.Token);
Console.WriteLine(registered ? "OK" : "FAILED");

Console.WriteLine("Агент работает. Ctrl+C для выхода.\n");

while (!cts.IsCancellationRequested)
{
    if (source.TryReadSample(out var sample))
    {
        batch.Add(sample);
        samplesSent++;
    }

    if (batch.Count >= 500 || DateTime.UtcNow - lastFlush >= TimeSpan.FromMilliseconds(options.SendIntervalMs))
    {
        await client.SendBatchAsync(batch, options.AgentId, cts.Token);
        batch.Clear();
        lastFlush = DateTime.UtcNow;
    }

    await Task.Delay(options.ReadIntervalMs, cts.Token);
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
    public int ReadIntervalMs { get; set; } = 100;
    public int SendIntervalMs { get; set; } = 1000;
    public bool UseFakeDataSource { get; set; } = false;
}