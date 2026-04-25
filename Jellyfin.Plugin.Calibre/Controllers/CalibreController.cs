using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Calibre.Controllers;

[ApiController]
[Route("[controller]")]
public class CalibreController : ControllerBase
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CalibreController> _logger;
    private readonly ILibraryManager _libraryManager;

    public CalibreController(ILoggerFactory loggerFactory, ILibraryManager libraryManager)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CalibreController>();
        _libraryManager = libraryManager;
    }

    [HttpGet("TestConnection")]
    public async Task<ActionResult<TestConnectionResult>> TestConnection(
        [FromQuery] string? serverUrl,
        [FromQuery] string? libraryPath,
        [FromQuery] bool? useHostAccessMode,
        [FromHeader(Name = "X-Calibre-Username")] string? username,
        [FromHeader(Name = "X-Calibre-Password")] string? password,
        CancellationToken ct)
    {
        var config = Plugin.Instance?.Configuration;
        var useHost = useHostAccessMode ?? config?.UseHostAccessMode ?? false;

        try
        {
            if (useHost)
            {
                if (string.IsNullOrWhiteSpace(serverUrl))
                {
                    return BadRequest(new TestConnectionResult(false, "serverUrl is required for host access mode."));
                }

                _logger.LogInformation("TestConnection (host mode) for {Url}", serverUrl);

                using var client = new CalibreContentServerClient(serverUrl, username, password);
                var ok = await client.TestConnectionAsync(ct).ConfigureAwait(false);

                return ok
                    ? new TestConnectionResult(true)
                    : new TestConnectionResult(false, "Server responded but credentials were rejected.");
            }
            else
            {
                var path = !string.IsNullOrWhiteSpace(libraryPath) ? libraryPath : config?.CalibreLibraryPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    _logger.LogInformation("TestConnection (local mode) with no libraryPath configured");
                    return BadRequest(new TestConnectionResult(false, "CalibreLibraryPath is required for local access mode."));
                }

                _logger.LogInformation("TestConnection (local mode) for {Path}", path);

                if (!Directory.Exists(path))
                {
                    return new TestConnectionResult(false, $"Library path does not exist: {path}");
                }

                var dbPath = Path.Combine(path, "metadata.db");
                if (!System.IO.File.Exists(dbPath))
                {
                    return new TestConnectionResult(false, $"metadata.db not found at: {dbPath}");
                }

                using var db = new CalibreDatabase(path);
                var ok = await db.TestConnectionAsync(ct).ConfigureAwait(false);

                return ok
                    ? new TestConnectionResult(true)
                    : new TestConnectionResult(false, "Could not read metadata.db.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestConnection failed");
            return Ok(new TestConnectionResult(false, ex.Message));
        }
    }

    [HttpGet("Libraries")]
    public ActionResult<GetLibrariesResponse> GetLibraries()
    {
        try
        {
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(lf => lf.CollectionType == CollectionTypeOptions.books)
                .Select(lf => new LibraryDto(
                    lf.ItemId.ToString(),
                    lf.Name ?? string.Empty,
                    "book"))
                .ToList();

            _logger.LogInformation("GetLibraries returned {Count} libraries", libraries.Count);
            return Ok(new GetLibrariesResponse(true, libraries, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Jellyfin libraries");
            return Ok(new GetLibrariesResponse(false, null, "Failed to fetch libraries"));
        }
    }

    [HttpGet("LibraryIds")]
    [Authorize(Policy = "DefaultAuthorization")]
    public ActionResult<GetLibraryIdsResponse> GetLibraryIds()
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            var configuredIds = config?.IncludedLibraryIds ?? new List<string>();

            if (configuredIds.Count > 0)
            {
                return Ok(new GetLibraryIdsResponse(configuredIds));
            }

            var allIds = _libraryManager.GetVirtualFolders()
                .Where(lf => lf.CollectionType.HasValue && lf.CollectionType.Value.ToString().Contains("book", StringComparison.OrdinalIgnoreCase))
                .Select(lf => lf.ItemId.ToString())
                .ToList();

            return Ok(new GetLibraryIdsResponse(allIds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get library IDs");
            return Ok(new GetLibraryIdsResponse(new List<string>()));
        }
    }

    [HttpGet("Config")]
    public ActionResult<CalibreConfigResult> GetConfig()
    {
        var cfg = Plugin.Instance?.Configuration;
        var useHostMode = cfg?.UseHostAccessMode ?? false;

        return Ok(new CalibreConfigResult(useHostMode, cfg?.CalibreLibraryPath ?? string.Empty, cfg?.CalibreServerUrl ?? string.Empty));
    }
}

public record TestConnectionResult(bool Success, string? Error = null);

public record GetLibrariesResponse(bool Success, List<LibraryDto>? Libraries = null, string? Error = null);

public record LibraryDto(string Id, string Name, string MediaType);

public record GetLibraryIdsResponse(List<string> LibraryIds);

public record CalibreConfigResult(bool UseHostAccessMode, string CalibreLibraryPath, string CalibreServerUrl);