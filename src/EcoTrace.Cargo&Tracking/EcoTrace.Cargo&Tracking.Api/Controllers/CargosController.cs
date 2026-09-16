using EcoTrace.Cargo_Tracking.Domain.DTOs;
using EcoTrace.Cargo_Tracking.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.Cargo_Tracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CargosController : ControllerBase
{
    private readonly ICargoService _cargoService;
    private readonly ILogger<CargosController> _logger;

    public CargosController(ICargoService cargoService, ILogger<CargosController> logger)
    {
        _cargoService = cargoService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? generatorTenantId, [FromQuery] Guid? carrierTenantId, CancellationToken cancellationToken)
    {
        return Ok(await _cargoService.GetAllAsync(generatorTenantId, carrierTenantId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var cargo = await _cargoService.GetByIdAsync(id, cancellationToken);
        return cargo is null ? NotFound() : Ok(cargo);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateCargoRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _cargoService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al crear el cargo");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCargoRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _cargoService.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al actualizar el cargo {CargoId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateCargoStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _cargoService.UpdateStatusAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al actualizar el estado del cargo {CargoId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _cargoService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}