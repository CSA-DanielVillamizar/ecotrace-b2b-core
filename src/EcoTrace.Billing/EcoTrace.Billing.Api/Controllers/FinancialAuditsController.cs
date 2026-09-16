using EcoTrace.Billing.Domain.Services;
using EcoTrace.Billing.Domain.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.Billing.Api.Controllers;

// Append-only por diseño (ADR 0001): solo lectura y creación, sin Update/Delete.
[ApiController]
[Route("api/[controller]")]
public class FinancialAuditsController : ControllerBase
{
    private readonly IFinancialAuditService _financialAuditService;
    private readonly ILogger<FinancialAuditsController> _logger;

    public FinancialAuditsController(IFinancialAuditService financialAuditService, ILogger<FinancialAuditsController> logger)
    {
        _financialAuditService = financialAuditService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _financialAuditService.GetAllAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var audit = await _financialAuditService.GetByIdAsync(id, cancellationToken);
        return audit is null ? NotFound() : Ok(audit);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateFinancialAuditRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _financialAuditService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Error de validación al crear la auditoría financiera");
            return BadRequest(new { error = ex.Message });
        }
    }
}
