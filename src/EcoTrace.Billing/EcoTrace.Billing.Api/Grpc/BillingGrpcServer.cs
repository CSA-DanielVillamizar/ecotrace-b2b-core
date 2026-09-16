using EcoTrace.Billing.Domain.DTOs;
using EcoTrace.Billing.Domain.Services;
using EcoTrace.Billing.Grpc;
using Grpc.Core;

namespace EcoTrace.Billing.Api.Grpc;

public class BillingGrpcServer : BillingGrpcService.BillingGrpcServiceBase
{
    private readonly IInvoiceService _invoiceService;

    public BillingGrpcServer(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public override async Task<GetInvoiceByIdResponse> GetInvoiceById(GetInvoiceByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            return new GetInvoiceByIdResponse { Found = false };
        }

        var invoice = await _invoiceService.GetByIdAsync(id, context.CancellationToken);
        return invoice is null
            ? new GetInvoiceByIdResponse { Found = false }
            : Map(invoice);
    }

    public override async Task<ListInvoicesResponse> ListInvoices(ListInvoicesRequest request, ServerCallContext context)
    {
        var invoices = await _invoiceService.GetAllAsync(context.CancellationToken);
        var response = new ListInvoicesResponse();
        response.Invoices.AddRange(invoices.Select(Map));
        return response;
    }

    private static GetInvoiceByIdResponse Map(InvoiceResponse invoice)
    {
        return new GetInvoiceByIdResponse
        {
            Id = invoice.Id.ToString(),
            InvoiceNumber = invoice.CargoId.ToString(),
            Status = "Created",
            Total = invoice.Amount.ToString("0.00"),
            CreatedAt = invoice.CreatedAt.ToString("O"),
            Found = true
        };
    }
}
