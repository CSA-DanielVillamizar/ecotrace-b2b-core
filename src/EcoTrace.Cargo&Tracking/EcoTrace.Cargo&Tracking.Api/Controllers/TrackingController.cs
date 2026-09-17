using EcoTrace.Cargo_Tracking.Domain.UseCases.Contracts;
using EcoTrace.Cargo_Tracking.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.Cargo_Tracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrackingController : ControllerBase
{
    private readonly ITrackingService _trackingService;
    private readonly ILogger<TrackingController> _logger;

    public TrackingController(ITrackingService trackingService, ILogger<TrackingController> logger)
    {
        _trackingService = trackingService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? cargoId, CancellationToken cancellationToken)
    {
        return Ok(await _trackingService.GetAllAsync(cargoId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var session = await _trackingService.GetByIdAsync(id, cancellationToken);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpGet("by-cargo/{cargoId:guid}")]
    public async Task<IActionResult> GetByCargoId(Guid cargoId, CancellationToken cancellationToken)
    {
        var session = await _trackingService.GetByCargoIdAsync(cargoId, cancellationToken);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartTracking(StartTrackingRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _trackingService.StartTrackingAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al iniciar el tracking");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/location")]
    public async Task<IActionResult> UpdateLocation(Guid id, UpdateTrackingLocationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _trackingService.UpdateLocationAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al actualizar el tracking {TrackingId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> CompleteTracking(Guid id, CancellationToken cancellationToken)
    {
        var completed = await _trackingService.CompleteTrackingAsync(id, cancellationToken);
        return completed is null ? NotFound() : Ok(completed);
    }
}