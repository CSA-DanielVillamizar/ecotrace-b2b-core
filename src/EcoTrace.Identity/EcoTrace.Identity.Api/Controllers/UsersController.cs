using EcoTrace.Identity.Domain.DTOs;
using EcoTrace.Identity.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        return Ok(await _userService.GetAllAsync(tenantId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userService.GetByIdAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _userService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al crear el usuario");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _userService.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al actualizar el usuario {UserId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _userService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/roles")]
    public async Task<IActionResult> AssignRole(Guid id, AssignRoleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var assigned = await _userService.AssignRoleAsync(id, request.RoleName, cancellationToken);
            return assigned ? Ok(new { message = $"Rol {request.RoleName} asignado correctamente." }) : BadRequest();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al asignar el rol al usuario {UserId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}/roles/{roleName}")]
    public async Task<IActionResult> RemoveRole(Guid id, string roleName, CancellationToken cancellationToken)
    {
        try
        {
            var removed = await _userService.RemoveRoleAsync(id, roleName, cancellationToken);
            return removed ? Ok(new { message = $"Rol {roleName} removido correctamente." }) : BadRequest();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al remover el rol del usuario {UserId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }
}