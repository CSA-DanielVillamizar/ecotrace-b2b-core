using EcoTrace.Cargo_Tracking.Domain.UseCases.Contracts;
using EcoTrace.Cargo_Tracking.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.Cargo_Tracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CargoAssignmentsController : ControllerBase
{
    private readonly ICargoAssignmentService _assignmentService;
    private readonly ILogger<CargoAssignmentsController> _logger;

    public CargoAssignmentsController(ICargoAssignmentService assignmentService, ILogger<CargoAssignmentsController> logger)
    {
        _assignmentService = assignmentService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? cargoId, [FromQuery] Guid? driverId, [FromQuery] Guid? vehicleId, CancellationToken cancellationToken)
    {
        return Ok(await _assignmentService.GetAllAsync(cargoId, driverId, vehicleId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var assignment = await _assignmentService.GetByIdAsync(id, cancellationToken);
        return assignment is null ? NotFound() : Ok(assignment);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateCargoAssignmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _assignmentService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al crear la asignación de carga");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateAssignmentStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _assignmentService.UpdateStatusAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al actualizar la asignación {AssignmentId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _assignmentService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}