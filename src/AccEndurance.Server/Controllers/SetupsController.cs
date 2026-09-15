using AccEndurance.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace AccEndurance.Server.Controllers;

[ApiController]
[Route("api/setups")]
public sealed class SetupsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SetupsController> _logger;

    public SetupsController(IConfiguration configuration, ILogger<SetupsController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string ResolveBasePath()
    {
        var custom = _configuration.GetValue<string>("Acc:SetupsBasePath");
        if (!string.IsNullOrEmpty(custom)) return custom;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Assetto Corsa Competizione",
            "Setups");
    }

    [HttpGet("base-path")]
    public IActionResult GetBasePath()
    {
        var basePath = ResolveBasePath();
        return Ok(new
        {
            basePath,
            exists = Directory.Exists(basePath),
        });
    }

    [HttpGet]
    public IActionResult ListCars()
    {
        var basePath = ResolveBasePath();
        if (!Directory.Exists(basePath)) return Ok(new List<object>());

        var cars = Directory.GetDirectories(basePath)
            .Select(d => new
            {
                CarFolder = Path.GetFileName(d),
                Tracks = Directory.GetDirectories(d).Select(t => Path.GetFileName(t)).ToList(),
            })
            .ToList();

        return Ok(cars);
    }

    [HttpGet("{car}")]
    public IActionResult ListTracks(string car)
    {
        var basePath = ResolveBasePath();
        var carPath = Path.Combine(basePath, car);
        if (!Directory.Exists(carPath)) return NotFound();

        var tracks = Directory.GetDirectories(carPath)
            .Select(t => new
            {
                TrackFolder = Path.GetFileName(t),
                Files = Directory.GetFiles(t, "*.json").Select(f => Path.GetFileName(f)).ToList(),
            })
            .ToList();

        return Ok(tracks);
    }

    [HttpGet("{car}/{track}/{file}")]
    public IActionResult GetSetup(string car, string track, string file)
    {
        var basePath = ResolveBasePath();
        var filePath = Path.Combine(basePath, car, track, file);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        var json = System.IO.File.ReadAllText(filePath);
        return Content(json, "application/json");
    }

    [HttpGet("browse/{car}/{track}")]
    public IActionResult BrowseSetups(string car, string track)
    {
        var basePath = ResolveBasePath();
        var trackPath = Path.Combine(basePath, car, track);
        if (!Directory.Exists(trackPath)) return Ok(new List<object>());

        var files = Directory.GetFiles(trackPath, "*.json")
            .Select(f => new
            {
                FileName = Path.GetFileName(f),
                ModifiedAt = System.IO.File.GetLastWriteTimeUtc(f),
                SizeBytes = new FileInfo(f).Length,
            })
            .OrderByDescending(f => f.ModifiedAt)
            .ToList();

        return Ok(files);
    }
}