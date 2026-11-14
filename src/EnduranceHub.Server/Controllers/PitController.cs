using EnduranceHub.Server.Hubs;
using EnduranceHub.Server.Services;
using EnduranceHub.Shared.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace EnduranceHub.Server.Controllers;

/// <summary>
/// План пит-стопа от споттера. ACC не позволяет удалённо менять пит-стратегию
/// чужой машины, поэтому план — это подсказка: он мгновенно уходит всем клиентам,
/// а пилот вводит значения в пит-меню (MFD).
/// </summary>
[ApiController]
[Route("api/pit")]
public sealed class PitController : ControllerBase
{
    private readonly SessionStateService _sessionState;
    private readonly IHubContext<SessionHub> _hub;

    public PitController(SessionStateService sessionState, IHubContext<SessionHub> hub)
    {
        _sessionState = sessionState;
        _hub = hub;
    }

    [HttpPost("plan")]
    public async Task<IActionResult> SetPlan([FromBody] PitPlanDto request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "Пустой план" });

        // Давления не обязательны, но если пришли — их должно быть ровно 4 (ПЛ/ПП/ЗЛ/ЗП).
        if (request.Pressures is { Length: > 0 and not 4 })
            return BadRequest(new { error = "Давлений должно быть 4" });

        var plan = _sessionState.SetPitPlan(request);

        // Отправляем сразу, не ожидая следующего тика фоновой рассылки:
        // план должен появиться у пилота в ту же секунду, даже если сессия ещё не активна.
        await _hub.Clients.All.SendAsync("StateUpdate", _sessionState.BuildSnapshot(), ct);
        return Ok(plan);
    }

    [HttpDelete("plan")]
    public async Task<IActionResult> ClearPlan([FromQuery] string? id, CancellationToken ct)
    {
        var cleared = _sessionState.ClearPitPlan(id);
        if (!cleared) return NotFound();
        await _hub.Clients.All.SendAsync("StateUpdate", _sessionState.BuildSnapshot(), ct);
        return NoContent();
    }
}
