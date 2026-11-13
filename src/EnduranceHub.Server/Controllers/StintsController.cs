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

    [HttpPost]
    public async Task<IActionResult> CreateStint([FromBody] CreateStintRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Driver))
            return BadRequest(new { error = "Укажите имя пилота" });
        if (request.StartedAt == default)
            return BadRequest(new { error = "Укажите время начала" });
        // Открытый ручной стинт сервер принял бы за текущий (GetCurrentStint), поэтому конец обязателен.
        if (request.EndedAt == null)
            return BadRequest(new { error = "Для нового стинта укажите время конца" });

        var stint = await _sessionState.CreateStintDetailsAsync(
            request.Driver, request.StartedAt, request.EndedAt, cancellationToken);
        return stint == null ? BadRequest(new { error = "Не удалось сохранить стинт" }) : Ok(stint);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteStint(int id, CancellationToken cancellationToken)
    {
        var deleted = await _sessionState.DeleteStintAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateStint(int id, [FromBody] UpdateStintRequest request,
        CancellationToken cancellationToken)
    {
        // Стинт может отсутствовать в памяти после перезапуска сервера — сервис обновит строку в БД.
        var stint = await _sessionState.UpdateStintDetailsAsync(
            id, request.Driver, request.StartedAt, request.EndedAt, cancellationToken);
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

    public sealed class CreateStintRequest
    {
        /// <summary>Имя пилота.</summary>
        public string? Driver { get; set; }
        /// <summary>Время начала стинта в UTC.</summary>
        public DateTime StartedAt { get; set; }
        /// <summary>Время конца стинта в UTC (для новой записи обязательно).</summary>
        public DateTime? EndedAt { get; set; }
    }
}