using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EcoTrace.CargoTracking.Infrastructure.Contracts;

/// <summary>
/// Contrato del evento EntregaConfirmada que Billing espera recibir.
/// </summary>
public sealed record EntregaConfirmadaRequest(
    string EventId,
    string EventType,
    DateTime OccurredAt,
    Guid CargaId,
    Guid VehiculoId,
    Guid ConductorId,
    Guid GeneradorTenantId,
    Guid TransportistaTenantId,
    string CorrelationId);
