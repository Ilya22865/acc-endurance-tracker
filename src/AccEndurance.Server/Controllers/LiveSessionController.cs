using AccEndurance.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace AccEndurance.Server.Controllers;

[ApiController]
[Route("api/live")]
public sealed class LiveSessionController : ControllerBase
{
    private readonly SessionStateService _sessionState;
    private readonly AccBroadcastService _broadcastService;

    public LiveSessionController(SessionStateService sessionState, AccBroadcastService broadcastService)
    {
        _sessionState = sessionState;
        _broadcastService = broadcastService;
    }

    [HttpGet("snapshot")]
    public IActionResult GetSnapshot()
    {
        return Ok(_sessionState.BuildSnapshot());
    }

    [HttpGet("outline")]
    public IActionResult GetOutline()
    {
        return Ok(_sessionState.RecordedOutline);
    }

    [HttpGet("connection")]
    public IActionResult GetConnection()
    {
        return Ok(new
        {
            Connected = _broadcastService.IsConnected,
        });
    }
}