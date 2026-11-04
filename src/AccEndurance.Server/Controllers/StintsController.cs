using AccEndurance.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace AccEndurance.Server.Controllers;

[ApiController]
[Route("api/stints")]
public sealed class StintsController : ControllerBase
{
    private readonly SessionStateService _sessionState;

    public StintsController(SessionStateService sessionState)
    {
        _sessionState = sessionState;
    }

    [HttpGet]
    public IActionResult GetStints()
    {
        return Ok(_sessionState.Stints);
    }

    [HttpGet("current")]
    public IActionResult GetCurrentStint()
    {
        var stint = _sessionState.Stints.LastOrDefault(s => s.EndedAt == null);
        return stint != null ? Ok(stint) : NoContent();
    }

    [HttpPatch("{id}")]
    public IActionResult UpdateStint(int id, [FromBody] UpdateStintRequest request)
    {
        // изменения применяются в памяти И сохраняются в БД
        var stint = _sessionState.UpdateStintDetails(
            id, request.Notes, request.PlannedFuelLiters, request.PlannedLaps);
        return stint == null ? NotFound() : Ok(stint);
    }

    public sealed class UpdateStintRequest
    {
        public string? Notes { get; set; }
        public double? PlannedFuelLiters { get; set; }
        public int? PlannedLaps { get; set; }
    }
}