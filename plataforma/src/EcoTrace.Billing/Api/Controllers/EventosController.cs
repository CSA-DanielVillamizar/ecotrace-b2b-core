using EcoTrace.Billing.Api.Contracts;
using EcoTrace.Billing.Api.Extensions;
using EcoTrace.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.Billing.Api.Controllers;

[ApiController]
[Route("api/eventos")]
[Produces("application/json")]
public sealed class EventosController(SagaRecepcionService recepcion) : ControllerBase
{
    [HttpPost("entrega-confirmada")]
    [ProducesResponseType<EventoAceptadoResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<EventoDuplicadoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecibirEntregaConfirmada(EntregaConfirmadaRequest solicitud, CancellationToken ct)
    {
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string
            ?? throw new InvalidOperationException("No se generó el identificador de correlación.");
        var resultado = await recepcion.RecibirAsync(solicitud, correlationId, ct);

        return resultado.Duplicado
            ? Ok(new EventoDuplicadoResponse("duplicado"))
            : Accepted(new EventoAceptadoResponse("aceptado", resultado.SagaId));
    }
}