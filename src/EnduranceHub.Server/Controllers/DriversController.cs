using EnduranceHub.Server.Services;
using EnduranceHub.Shared.Broadcasting;
using Microsoft.AspNetCore.Mvc;

namespace EnduranceHub.Server.Controllers;

[ApiController]
[Route("api/drivers")]
public sealed class DriversController : ControllerBase
{
    private readonly SessionStateService _sessionState;

    public DriversController(SessionStateService sessionState)
    {
        _sessionState = sessionState;
    }

    [HttpGet("entry-list")]
    public IActionResult GetEntryList()
    {
        var snapshot = _sessionState.BuildSnapshot();
        var carStates = snapshot.Cars
            .Select(c => new
            {
                c.CarIndex,
                c.RaceNumber,
                c.TeamName,
                c.CarModelType,
                c.CarModelName,
                c.Driver,
                c.DriverIndex,
                c.Position,
            })
            .ToList();
        return Ok(carStates);
    }

    [HttpGet("team")]
    public IActionResult GetTeamDrivers()
    {
        var drivers = _sessionState.GetTeamDrivers();
        return Ok(drivers);
    }

    [HttpGet]
    public IActionResult GetAllDrivers()
    {
        return Ok(_sessionState.GetAllDrivers());
    }

    [HttpGet("our-car")]
    public IActionResult GetOurCar()
    {
        var snapshot = _sessionState.BuildSnapshot();
        var ourCar = snapshot.Cars.FirstOrDefault(c => c.IsOurCar);
        return ourCar != null ? Ok(ourCar) : NotFound("Our car not found in entry list");
    }

    [HttpGet("current-driver")]
    public IActionResult GetCurrentDriver()
    {
        var stint = _sessionState.Stints.LastOrDefault(s => s.EndedAt == null);
        return stint != null
            ? Ok(new { stint.Driver, stint.DriverIndex, stint.StartedAt })
            : Ok(new { Driver = "N/A", DriverIndex = -1, StartedAt = (DateTime?)null });
    }
}