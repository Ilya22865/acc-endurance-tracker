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
        var stint = _sessionState.Stints.LastOrDefault(s => s.Id == id);
        if (stint == null) return NotFound();

        if (request.Notes != null) stint.Notes = request.Notes;
        if (request.PlannedFuelLiters.HasValue) stint.PlannedFuelLiters = request.PlannedFuelLiters;
        if (request.PlannedLaps.HasValue) stint.PlannedLaps = request.PlannedLaps;
        return Ok(stint);
    }

    public sealed class UpdateStintRequest
    {
        public string? Notes { get; set; }
        public double? PlannedFuelLiters { get; set; }
        public int? PlannedLaps { get; set; }
    }
}