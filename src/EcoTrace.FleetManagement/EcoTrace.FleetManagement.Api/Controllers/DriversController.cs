using EcoTrace.FleetManagement.Domain.UseCases.Contracts;
using EcoTrace.FleetManagement.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.FleetManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DriversController : ControllerBase
{
    private readonly IDriverService _driverService;
    private readonly ILogger<DriversController> _logger;

    public DriversController(IDriverService driverService, ILogger<DriversController> logger)
    {
        _driverService = driverService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        return Ok(await _driverService.GetAllAsync(tenantId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var driver = await _driverService.GetByIdAsync(id, cancellationToken);
        return driver is null ? NotFound() : Ok(driver);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateDriverRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _driverService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al crear el conductor");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateDriverRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _driverService.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al actualizar el conductor {DriverId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateDriverStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _driverService.UpdateStatusAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al actualizar el estado del conductor {DriverId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _driverService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}