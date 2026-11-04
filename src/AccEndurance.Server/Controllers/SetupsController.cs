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

    /// <summary>
    /// Собирает путь внутри папки сетапов и гарантирует, что он из неё не выходит.
    /// Раньше car/track/file подставлялись в Path.Combine как есть: "..", абсолютный путь или
    /// "%5C" позволяли прочитать ЛЮБОЙ файл на сервере (а он слушает 0.0.0.0).
    /// </summary>
    private bool TryResolveInside(out string result, params string[] parts)
    {
        result = "";
        var basePath = Path.GetFullPath(ResolveBasePath());
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)
                || part == "." || part == ".."
                || part.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;
        }

        var full = Path.GetFullPath(Path.Combine(new[] { basePath }.Concat(parts).ToArray()));
        var prefix = basePath.EndsWith(Path.DirectorySeparatorChar)
            ? basePath
            : basePath + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        result = full;
        return true;
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
        if (!TryResolveInside(out var carPath, car)) return BadRequest("invalid path");
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
        if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return BadRequest("only .json setups");
        if (!TryResolveInside(out var filePath, car, track, file)) return BadRequest("invalid path");
        if (!System.IO.File.Exists(filePath)) return NotFound();

        var json = System.IO.File.ReadAllText(filePath);
        return Content(json, "application/json");
    }

    [HttpGet("browse/{car}/{track}")]
    public IActionResult BrowseSetups(string car, string track)
    {
        if (!TryResolveInside(out var trackPath, car, track)) return BadRequest("invalid path");
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