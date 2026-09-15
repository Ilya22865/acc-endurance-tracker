using AccEndurance.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace AccEndurance.Server.Controllers;

[ApiController]
[Route("api/telemetry")]
public sealed class TelemetryController : ControllerBase
{
    private readonly SessionStateService _sessionState;

    public TelemetryController(SessionStateService sessionState)
    {
        _sessionState = sessionState;
    }

    [HttpPost("register")]
    public IActionResult RegisterDriver([FromBody] AgentRegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId)) return BadRequest("agentId required");
        _sessionState.RegisterDriver(request.AgentId, request.DriverIndex, request.DriverName ?? "");
        return Ok(new { registered = true });
    }

    [HttpPost("batch")]
    public IActionResult ReceiveBatch([FromBody] AgentBatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DriverId)) return BadRequest("driverId required");
        if (request.Samples == null || request.Samples.Count == 0) return Ok(new { accepted = 0 });

        var readings = request.Samples
            .Select(s => (s.Gas, s.Brake, s.SteerAngle))
            .ToList();

        var accepted = _sessionState.AcceptTelemetryBatch(request.DriverId, readings);
        return Ok(new { received = readings.Count, accepted = accepted.Count });
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