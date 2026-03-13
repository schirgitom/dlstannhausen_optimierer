using Microsoft.AspNetCore.Mvc;
using SoWeiT.Optimizer.Service.Contracts;
using SoWeiT.Optimizer.Service.Services;

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

        if (!_sessionService.TryPrepareData(resolvedSessionId, request, out var data) || data is null)
        {
            return SessionNotFound(resolvedSessionId);
        }

        SetSessionCookie(resolvedSessionId);
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

        if (!_sessionService.TryPreprocessing(resolvedSessionId, request, out var result, out var validationError))
        {
            return SessionNotFound(resolvedSessionId);
        }

        if (validationError is not null)
        {
            return BadRequest(new { Message = validationError });
        }

        SetSessionCookie(resolvedSessionId);
        if (result is null)
        {
            return BadRequest(new { Message = "Preprocessing result is missing." });
        }

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

        if (!_sessionService.TryPostprocessing(resolvedSessionId, request))
        {
            return SessionNotFound(resolvedSessionId);
        }

        SetSessionCookie(resolvedSessionId);
        return NoContent();
    }

    [HttpPost("{sessionId:guid}/run")]
    [HttpPost("current/run")]
    public ActionResult<RunResponse> Run([FromBody] RunRequest request, Guid? sessionId = null)
    {
        if (!TryResolveSessionId(sessionId, out var resolvedSessionId, out var error))
        {
            if (!TryRunWithRecoveredSession(request, out resolvedSessionId, out var recoveredResponse, out var recoveredValidationError))
            {
                if (recoveredValidationError is not null)
                {
                    return BadRequest(new { Message = recoveredValidationError });
                }

                return error;
            }

            SetSessionTracking(resolvedSessionId);
            return Ok(recoveredResponse);
        }

        if (!_sessionService.TryRun(resolvedSessionId, request, out var response, out var validationError))
        {
            if (!TryRunWithRecoveredSession(request, out resolvedSessionId, out response, out validationError))
            {
                return SessionNotFound(resolvedSessionId);
            }
        }

        if (validationError is not null)
        {
            return BadRequest(new { Message = validationError });
        }

        SetSessionTracking(resolvedSessionId);
        if (response is null)
        {
            return BadRequest(new { Message = "Run response is missing." });
        }

        return Ok(response);
    }

    [HttpGet("{sessionId:guid}/state")]
    [HttpGet("current/state")]
    public ActionResult<OptimizerStateResponse> GetState(Guid? sessionId = null)
    {
        if (!TryResolveSessionId(sessionId, out var resolvedSessionId, out var error))
        {
            return error;
        }

        if (!_sessionService.TryGetState(resolvedSessionId, out var state) || state is null)
        {
            return SessionNotFound(resolvedSessionId);
        }

        SetSessionCookie(resolvedSessionId);
        return Ok(state);
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

    private ActionResult SessionNotFound(Guid sessionId)
    {
        Response.Cookies.Delete(SessionCookieName);
        return NotFound(new { Message = $"Session {sessionId} not found or expired." });
    }

    private bool TryRunWithRecoveredSession(
        RunRequest request,
        out Guid sessionId,
        out RunResponse? response,
        out string? validationError)
    {
        response = null;
        validationError = null;
        if (!_sessionService.TryCreateSessionForRunRecovery(request, out sessionId, out validationError))
        {
            return false;
        }

        if (!_sessionService.TryRun(sessionId, request, out response, out validationError))
        {
            return false;
        }

        _logger.LogInformation("Run recovered with new session {SessionId}", sessionId);
        return true;
    }

    private void SetSessionTracking(Guid sessionId)
    {
        Response.Headers["X-Optimizer-Session-Id"] = sessionId.ToString("D");
        SetSessionCookie(sessionId);
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

}

