using System.Net;

namespace EnduranceHub.Shared.Net;

/// <summary>
/// HTTP-настройки для связи с сервером EnduranceHub.
///
/// Зачем это нужно. VPN-клиенты (Hiddify, v2rayN, Nekoray, Clash и похожие)
/// при включении прописывают в Windows системный прокси. .NET HttpClient,
/// ClientWebSocket и SignalR по умолчанию ходят через этот прокси, поэтому
/// после включения VPN новые соединения с сервером уходят в прокси и рвутся
/// (сервер находится в локальной или оверлейной сети Radmin/ZeroTier, куда
/// прокси не умеет). Уже открытое соединение при этом продолжает работать —
/// отсюда «выключил VPN, подключился, включил обратно — работает».
///
/// Трафику EnduranceHub прокси не нужен никогда, поэтому ходим напрямую.
/// Это не глобальная настройка: проверка обновлений (GitHub) продолжает
/// использовать системный прокси и работает через VPN как раньше.
/// </summary>
public static class DirectHttp
{
    public static SocketsHttpHandler CreateHandler() => new()
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        // Не держим сокет вечно: после смены сети/маршрутов старый может быть мёртв.
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        AutomaticDecompression = DecompressionMethods.All,
    };

    public static HttpClient CreateClient(TimeSpan timeout)
        => new(CreateHandler()) { Timeout = timeout };
}

/// <summary>Прокси, который ничего не проксирует: IsBypassed всегда true.</summary>
public sealed class NoProxy : IWebProxy
{
    public static readonly NoProxy Instance = new();

    public ICredentials? Credentials { get; set; }

    public Uri GetProxy(Uri destination) => destination;

    public bool IsBypassed(Uri host) => true;
}
