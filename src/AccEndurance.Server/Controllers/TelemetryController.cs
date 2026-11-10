using AccEndurance.Server.Hubs;
using AccEndurance.Server.Services;
using AccEndurance.Shared.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AccEndurance.Server.Controllers;

[ApiController]
[Route("api/telemetry")]
public sealed class TelemetryController : ControllerBase
{
    private readonly SessionStateService _sessionState;
    private readonly IHubContext<SessionHub> _hub;

    public TelemetryController(SessionStateService sessionState, IHubContext<SessionHub> hub)
    {
        _sessionState = sessionState;
        _hub = hub;
    }

    [HttpPost("register")]
    public IActionResult RegisterDriver([FromBody] AgentRegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId)) return BadRequest("agentId required");
        _sessionState.RegisterDriver(request.AgentId, request.DriverIndex, request.DriverName ?? "");
        return Ok(new { registered = true });
    }

    [HttpPost("batch")]
    public async Task<IActionResult> ReceiveBatch([FromBody] AgentBatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DriverId)) return BadRequest("driverId required");

        var readings = (request.Samples ?? new List<AgentSample>())
            .Select(s => (s.Gas, s.Brake, s.SteerAngle))
            .ToList();

        // Пустая пачка тоже проходит проверку регистрации — агент шлёт «только флаги» даже когда ACC на паузе
        var result = _sessionState.AcceptTelemetryBatch(request.DriverId, readings);

        // 409 — сигнал агенту, что регистрация потеряна (например, сервер перезапустили): нужно зарегистрироваться заново
        if (!result.Registered)
            return StatusCode(StatusCodes.Status409Conflict, new { error = "agent not registered" });

        if (request.Flags != null && result.IsCurrentDriver)
        {
            _sessionState.UpdateAgentFlags(request.DriverId, new AgentFlagsState
            {
                Status = request.Flags.Status,
                PlayerFlag = request.Flags.PlayerFlag,
                Yellow = request.Flags.Yellow,
                Yellow1 = request.Flags.Yellow1,
                Yellow2 = request.Flags.Yellow2,
                Yellow3 = request.Flags.Yellow3,
                White = request.Flags.White,
                Green = request.Flags.Green,
                Chequered = request.Flags.Chequered,
                Red = request.Flags.Red,
            });
        }

        // «живая» телеметрия — сразу всем клиентам, но только от пилота, который сейчас за рулём.
        // Иначе при смене пилотов два агента смешивают данные на графиках.
        if (result.Samples.Count > 0 && result.IsCurrentDriver)
        {
            await _hub.Clients.All.SendAsync("TelemetryBatch", new TelemetryBatchDto
            {
                Driver = result.Driver,
                DriverIndex = result.DriverIndex,
                Samples = result.Samples.ToList(),
            });
        }

        return Ok(new
        {
            received = readings.Count,
            live = result.Samples.Count,
            accepted = result.Persisted ? result.Samples.Count : 0,
        });
    }

    public sealed class AgentRegisterRequest
    {
        public string AgentId { get; set; } = "";
        public int DriverIndex { get; set; }
        public string DriverName { get; set; } = "";
    }

    public sealed class AgentBatchRequest
    {
        public string DriverId { get; set; } = "";
        public List<AgentSample>? Samples { get; set; }
        public AgentFlags? Flags { get; set; }
    }

    public sealed class AgentFlags
    {
        public int Status { get; set; }
        public int PlayerFlag { get; set; }
        public bool Yellow { get; set; }
        public bool Yellow1 { get; set; }
        public bool Yellow2 { get; set; }
        public bool Yellow3 { get; set; }
        public bool White { get; set; }
        public bool Green { get; set; }
        public bool Chequered { get; set; }
        public bool Red { get; set; }
    }

    public sealed class AgentSample
    {
        public float Gas { get; set; }
        public float Brake { get; set; }
        public float SteerAngle { get; set; }
    }
}