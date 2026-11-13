using EnduranceHub.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnduranceHub.Server.Controllers;

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
            id, request.Driver, request.StartedAt, request.EndedAt);
        return stint == null ? NotFound() : Ok(stint);
    }

    public sealed class UpdateStintRequest
    {
        /// <summary>Имя пилота (пустое значение не меняет поле).</summary>
        public string? Driver { get; set; }
        /// <summary>Время начала стинта в UTC.</summary>
        public DateTime? StartedAt { get; set; }
        /// <summary>Время конца стинта в UTC (null — не менять).</summary>
        public DateTime? EndedAt { get; set; }
    }
}