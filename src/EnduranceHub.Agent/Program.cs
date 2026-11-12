using EnduranceHub.Agent;
using System.Diagnostics;

var options = ParseArgs(args);

Console.WriteLine("EnduranceHub Agent v1.0");
Console.WriteLine($"  Сервер:   {options.ServerUrl}");
Console.WriteLine($"  Agent ID: {options.AgentId}");

// Сколько сэмплов держим при обрыве связи. 2000 при ~40 Гц — это около 50 секунд:
// переживаем переключение VPN/сети, но не копим бесполезные старые данные бесконечно.
const int MaxBufferedSamples = 2000;
// Пачку шлём небольшими кусками: большой JSON (500+ сэмплов) хуже проходит через
// VPN/фрагментацию и дольше висит при таймауте.
const int MaxBatchSize = 200;

var pending = new Queue<TelemetryReading>(MaxBufferedSamples);
var driverIndex = -1;
var driverName = "";
var driverId = ""; // уникальный ID для этого пилота
var lastFlush = DateTime.UtcNow;
var sendAllowedAt = DateTime.MinValue;
var consecutiveFailures = 0;
var linkDown = false;
var samplesSent = 0L;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Если клиент, запустивший агент, упал, агент не должен остаться висеть
// и продолжать слать телеметрию. Проверяем родителя раз в 2 секунды.
if (options.ParentPid > 0)
{
    _ = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                using var parent = Process.GetProcessById(options.ParentPid);
                if (parent.HasExited) break;
            }
            catch (ArgumentException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }
            catch
            {
                // временная ошибка — проверим ещё раз
            }

            try
            {
                await Task.Delay(2000, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (!cts.IsCancellationRequested)
        {
            Console.WriteLine($"Родительский процесс {options.ParentPid} завершился — останавливаю агент.");
            cts.Cancel();
        }
    });
}

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

var nextRegister = DateTime.UtcNow + (registered ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5));
var lastPacketId = int.MinValue;

try
{
    while (!cts.IsCancellationRequested)
    {
        try
        {
            // читаем часто (≈40 Гц) и пропускаем повторы: графики в клиенте строятся в реальном времени
            if (source.TryReadSample(out var sample) && sample.PacketId != lastPacketId)
            {
                lastPacketId = sample.PacketId;
                // при переполнении выбрасываем самый старый сэмпл: в графиках нужны свежие данные
                if (pending.Count >= MaxBufferedSamples) pending.Dequeue();
                pending.Enqueue(sample);
                samplesSent++;
            }

            // Регистрация хранится на сервере в памяти и пропадает при его перезапуске
            // (после этого батчи молча отбрасывались). Повторяем её периодически — запрос идемпотентный.
            if (DateTime.UtcNow >= nextRegister)
            {
                var ok = await client.RegisterAsync(driverIndex, driverName, cts.Token);
                nextRegister = DateTime.UtcNow + (ok ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5));
            }

            var flushDue = pending.Count >= 500
                || DateTime.UtcNow - lastFlush >= TimeSpan.FromMilliseconds(options.SendIntervalMs);

            if (flushDue && DateTime.UtcNow >= sendAllowedAt)
            {
                // флаги отправляем с каждой пачкой, даже пустой (ACC на паузе / в меню — сэмплов нет, флаги нужны)
                FlagReading? flags = source.TryReadFlags(out var fr) ? fr : null;

                var count = Math.Min(pending.Count, MaxBatchSize);
                var chunk = new List<TelemetryReading>(count);
                if (count > 0)
                {
                    var i = 0;
                    foreach (var reading in pending)
                    {
                        if (i++ >= count) break;
                        chunk.Add(reading);
                    }
                }

                var result = await client.SendBatchAsync(chunk, flags, cts.Token);
                lastFlush = DateTime.UtcNow;

                if (result == SendResult.Sent)
                {
                    for (var i = 0; i < count; i++) pending.Dequeue();
                    sendAllowedAt = DateTime.MinValue;
                    consecutiveFailures = 0;

                    if (linkDown)
                    {
                        linkDown = false;
                        Console.WriteLine($"[net] Связь с сервером восстановлена ({options.ServerUrl}).");
                    }
                }
                else
                {
                    consecutiveFailures++;

                    if (!linkDown)
                    {
                        linkDown = true;
                        Console.WriteLine($"[net] Нет связи с сервером ({options.ServerUrl}). " +
                            $"Телеметрия буферизуется ({pending.Count} сэмплов), переподключусь автоматически.");
                    }

                    // 409 — сервер потерял регистрацию (перезапуск): регистрируемся сразу.
                    if (result == SendResult.Rejected)
                        nextRegister = DateTime.MinValue;

                    // 0.25, 0.5, 1, 2, 3, 3… секунд: не долбим сервер и не тратим CPU на таймауты.
                    var backoffMs = Math.Min(3000, 250 * Math.Pow(2, Math.Min(consecutiveFailures - 1, 4)));
                    sendAllowedAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(backoffMs);
                }
            }

            await Task.Delay(options.ReadIntervalMs, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Раньше любая ошибка чтения shared memory или сети убивала агент молча.
            // Теперь пишем её в agent.log и продолжаем работу.
            Console.WriteLine($"[error] {ex}");
            await Task.Delay(1000, cts.Token);
        }
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C или завершение родительского процесса — штатный выход
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
            case "--parent-pid": options.ParentPid = int.Parse(args[++i]); break;
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
    // 50 мс (≈20 пачек/с) вместо 250 мс: клиент получает данные почти непрерывно,
    // поэтому графики и радар не «дёргаются» раз в четверть секунды.
    public int SendIntervalMs { get; set; } = 50;
    public bool UseFakeDataSource { get; set; } = false;
    public int ParentPid { get; set; } =
        int.TryParse(
            Environment.GetEnvironmentVariable("ENDURANCEHUB_PARENT_PID")
            ?? Environment.GetEnvironmentVariable("ACC_PARENT_PID"),
            out var pid) ? pid : 0;
}