using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EcoTrace.Billing.Domain;

public sealed class OperacionPago
{
    public Guid OperacionPagoId { get; private set; }
    public Guid PagoId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Operacion { get; private set; } = string.Empty;
    public DateTime CreadaEn { get; private set; }
}
