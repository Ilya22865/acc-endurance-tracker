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
        if (request.Samples == null || request.Samples.Count == 0) return Ok(new { accepted = 0 });

        var readings = request.Samples
            .Select(s => (s.Gas, s.Brake, s.SteerAngle))
            .ToList();

        var result = _sessionState.AcceptTelemetryBatch(request.DriverId, readings);

        // 409 — сигнал агенту, что регистрация потеряна (например, сервер перезапустили): нужно зарегистрироваться заново
        if (!result.Registered)
            return StatusCode(StatusCodes.Status409Conflict, new { error = "agent not registered" });

        // «живая» телеметрия — сразу всем клиентам
        if (result.Samples.Count > 0)
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
    }

    public sealed class AgentSample
    {
        public float Gas { get; set; }
        public float Brake { get; set; }
        public float SteerAngle { get; set; }
    }
}