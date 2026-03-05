using Microsoft.AspNetCore.Mvc;
using SoWeiT.Optimizer;
using SoWeiT.Optimizer.Api.Contracts;
using SoWeiT.Optimizer.Api.Persistence;
using SoWeiT.Optimizer.Api.Services;

namespace SoWeiT.Optimizer.Api.Controllers;

[ApiController]
[Route("api/optimizer-sessions")]
public sealed class OptimizerSessionsController : ControllerBase
{
    private const string SessionCookieName = "optimizer-session-id";

    private readonly OptimizerSessionService _sessionService;
    private readonly ILogger<OptimizerSessionsController> _logger;

    public OptimizerSessionsController(OptimizerSessionService sessionService, ILogger<OptimizerSessionsController> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    [HttpPost]
    public ActionResult<CreateOptimizerSessionResponse> Create([FromBody] CreateOptimizerSessionRequest request)
    {
        _logger.LogInformation("HTTP Create called");
        var sessionId = _sessionService.Create(request);
        SetSessionCookie(sessionId);
        return Ok(new CreateOptimizerSessionResponse(sessionId));
    }

    [HttpDelete("{sessionId:guid}")]
    public IActionResult Delete(Guid sessionId)
    {
        _logger.LogInformation("HTTP Delete called for session {SessionId}", sessionId);
        var deleted = _sessionService.Delete(sessionId);
        if (deleted)
        {
            ClearSessionCookieIfMatches(sessionId);
            return NoContent();
        }

        return NotFound();
    }

    [HttpDelete("current")]
    public IActionResult DeleteCurrent()
    {
        if (!TryResolveSessionId(null, out var sessionId, out var error))
        {
            return error;
        }

        return Delete(sessionId);
    }

    [HttpPost("{sessionId:guid}/prepare-data")]
    [HttpPost("current/prepare-data")]
    public IActionResult PrepareData([FromBody] PrepareDataRequest request, Guid? sessionId = null)
    {
        if (!TryResolveSessionId(sessionId, out var resolvedSessionId, out var error))
        {
            return error;
        }

        if (!TryGetOptimizer(resolvedSessionId, out var optimizer, out var notFound))
        {
            return notFound;
        }

        SetSessionCookie(resolvedSessionId);
        var data = optimizer.PrepareData(request.Verbrauch);
        return Ok(data);
    }

    [HttpPost("{sessionId:guid}/preprocessing")]
    [HttpPost("current/preprocessing")]
    public IActionResult Preprocessing([FromBody] PreprocessingRequest request, Guid? sessionId = null)
    {
        if (!TryResolveSessionId(sessionId, out var resolvedSessionId, out var error))
        {
            return error;
        }

        if (!TryGetOptimizer(resolvedSessionId, out var optimizer, out var notFound))
        {
            return notFound;
        }

        SetSessionCookie(resolvedSessionId);
        var result = optimizer.Preprocessing(request.Zeitstempel, request.Verbrauch);
        _sessionService.PersistMutation(
            resolvedSessionId,
            optimizer,
            "preprocessing",
            request.Zeitstempel,
            optimizer.Erzeugung,
            BuildPreprocessingUserLogs(request.Verbrauch, result));
        return Ok(result);
    }

    [HttpPost("{sessionId:guid}/postprocessing")]
    [HttpPost("current/postprocessing")]
    public IActionResult Postprocessing([FromBody] PostprocessingRequest request, Guid? sessionId = null)
    {
        if (!TryResolveSessionId(sessionId, out var resolvedSessionId, out var error))
        {
            return error;
        }

        if (!TryGetOptimizer(resolvedSessionId, out var optimizer, out var notFound))
        {
            return notFound;
        }

        SetSessionCookie(resolvedSessionId);
        optimizer.Postprocessing(request.Zeitstempel);
        _sessionService.PersistMutation(
            resolvedSessionId,
            optimizer,
            "postprocessing",
            request.Zeitstempel,
            optimizer.Erzeugung);
        return NoContent();
    }

    [HttpPost("{sessionId:guid}/run")]
    [HttpPost("current/run")]
    public ActionResult<RunResponse> Run([FromBody] RunRequest request, Guid? sessionId = null)
    {
        if (!TryResolveSessionId(sessionId, out var resolvedSessionId, out var error))
        {
            return error;
        }

        if (!TryGetOptimizer(resolvedSessionId, out var optimizer, out var notFound))
        {
            return notFound;
        }

        SetSessionCookie(resolvedSessionId);
        var result = optimizer.Run(request.PvErzeugungWatt, request.Verbrauch, request.Zeitstempel);
        _sessionService.PersistMutation(
            resolvedSessionId,
            optimizer,
            "run",
            request.Zeitstempel,
            request.PvErzeugungWatt,
            BuildRunUserLogs(request.Verbrauch, result.Schaltzustand));
        return Ok(new RunResponse(result.Schaltzustand, result.ResOpt, result.ResOpt));
    }

    [HttpPost("{sessionId:guid}/update-verteilung")]
    [HttpPost("current/update-verteilung")]
    public IActionResult UpdateVerteilung([FromBody] UpdateVerteilungRequest request, Guid? sessionId = null)
    {
        if (!TryResolveSessionId(sessionId, out var resolvedSessionId, out var error))
        {
            return error;
        }

        if (!TryGetOptimizer(resolvedSessionId, out var optimizer, out var notFound))
        {
            return notFound;
        }

        SetSessionCookie(resolvedSessionId);
        optimizer.UpdateVerteilung(request.PvVerbrauchDeltas, request.VerbrauchDeltas);
        _sessionService.PersistMutation(
            resolvedSessionId,
            optimizer,
            "update_verteilung",
            DateTimeOffset.UtcNow,
            optimizer.Erzeugung);
        return NoContent();
    }

    [HttpPost("{sessionId:guid}/update-verteilung-mittels-energie")]
    [HttpPost("current/update-verteilung-mittels-energie")]
    public IActionResult UpdateVerteilungMittelsEnergie([FromBody] UpdateVerteilungMittelsEnergieRequest request, Guid? sessionId = null)
    {
        if (!TryResolveSessionId(sessionId, out var resolvedSessionId, out var error))
        {
            return error;
        }

        if (!TryGetOptimizer(resolvedSessionId, out var optimizer, out var notFound))
        {
            return notFound;
        }

        SetSessionCookie(resolvedSessionId);
        optimizer.UpdateVerteilungMittelsEnergie(request.PvVerbrauchEnergieStand, request.VerbrauchEnergieStand);
        _sessionService.PersistMutation(
            resolvedSessionId,
            optimizer,
            "update_verteilung_mittels_energie",
            DateTimeOffset.UtcNow,
            optimizer.Erzeugung);
        return NoContent();
    }

    [HttpGet("{sessionId:guid}/state")]
    [HttpGet("current/state")]
    public ActionResult<OptimizerStateResponse> GetState(Guid? sessionId = null)
    {
        if (!TryResolveSessionId(sessionId, out var resolvedSessionId, out var error))
        {
            return error;
        }

        if (!TryGetOptimizer(resolvedSessionId, out var optimizer, out var notFound))
        {
            return notFound;
        }

        SetSessionCookie(resolvedSessionId);
        return Ok(new OptimizerStateResponse(
            optimizer.N,
            optimizer.Sperrzeit1,
            optimizer.Sperrzeit2,
            optimizer.Erzeugung,
            optimizer.Faktor.ToArray(),
            ToJagged(optimizer.Verteilung),
            optimizer.Schaltkontingent.ToArray(),
            optimizer.Schaltzeit.ToArray(),
            ToJagged(optimizer.Schaltzustand),
            optimizer.PvVerbrauchEnergieStand?.ToArray(),
            optimizer.VerbrauchEnergieStand?.ToArray()));
    }

    private bool TryResolveSessionId(Guid? routeSessionId, out Guid sessionId, out ActionResult error)
    {
        if (routeSessionId.HasValue)
        {
            sessionId = routeSessionId.Value;
            error = null!;
            return true;
        }

        if (Request.Cookies.TryGetValue(SessionCookieName, out var cookieValue) && Guid.TryParse(cookieValue, out sessionId))
        {
            error = null!;
            return true;
        }

        sessionId = Guid.Empty;
        error = BadRequest(new { Message = "No session id in route or cookie." });
        return false;
    }

    private bool TryGetOptimizer(Guid sessionId, out Optimierer optimizer, out ActionResult notFound)
    {
        _logger.LogInformation("TryGetOptimizer for session {SessionId}", sessionId);

        if (_sessionService.TryGet(sessionId, out var found) && found is not null)
        {
            optimizer = found;
            notFound = null!;
            return true;
        }

        optimizer = null!;
        Response.Cookies.Delete(SessionCookieName);
        notFound = NotFound(new { Message = $"Session {sessionId} not found or expired." });
        return false;
    }

    private void SetSessionCookie(Guid sessionId)
    {
        Response.Cookies.Append(
            SessionCookieName,
            sessionId.ToString("D"),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = false,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
    }

    private void ClearSessionCookieIfMatches(Guid sessionId)
    {
        if (!Request.Cookies.TryGetValue(SessionCookieName, out var cookieValue))
        {
            return;
        }

        if (Guid.TryParse(cookieValue, out var cookieSessionId) && cookieSessionId == sessionId)
        {
            Response.Cookies.Delete(SessionCookieName);
        }
    }

    private static double[][] ToJagged(double[,] matrix)
    {
        var rows = matrix.GetLength(0);
        var cols = matrix.GetLength(1);
        var result = new double[rows][];

        for (var r = 0; r < rows; r++)
        {
            result[r] = new double[cols];
            for (var c = 0; c < cols; c++)
            {
                result[r][c] = matrix[r, c];
            }
        }

        return result;
    }

    private static IReadOnlyList<OptimizerRequestUserLog> BuildPreprocessingUserLogs(double[] requiredPowerWatt, double[] preprocessedResult)
    {
        var users = new List<OptimizerRequestUserLog>(requiredPowerWatt.Length);
        for (var i = 0; i < requiredPowerWatt.Length; i++)
        {
            var isSwitchAllowed = requiredPowerWatt[i] <= 0.0 || preprocessedResult[i] > 0.0;
            users.Add(new OptimizerRequestUserLog(i, requiredPowerWatt[i], isSwitchAllowed));
        }

        return users;
    }

    private static IReadOnlyList<OptimizerRequestUserLog> BuildRunUserLogs(double[] requiredPowerWatt, double[] switchState)
    {
        var users = new List<OptimizerRequestUserLog>(requiredPowerWatt.Length);
        for (var i = 0; i < requiredPowerWatt.Length; i++)
        {
            users.Add(new OptimizerRequestUserLog(i, requiredPowerWatt[i], switchState[i] > 0.0));
        }

        return users;
    }
}
