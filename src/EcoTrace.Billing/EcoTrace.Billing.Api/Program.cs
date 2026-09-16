using EcoTrace.Billing.Api.Grpc;
using EcoTrace.Billing.Api.ErrorHandling;
using EcoTrace.Billing.Domain.Repositories;
using EcoTrace.Billing.Domain.Services;
using EcoTrace.Billing.Infrastructure.Data;
using EcoTrace.Billing.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionHandlingInterceptor>();
});
builder.Services.AddControllers();

builder.Services.AddDbContext<BillingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BillingDatabase")));

builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IFinancialAuditRepository, FinancialAuditRepository>();

builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IFinancialAuditService, FinancialAuditService>();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapGrpcService<BillingGrpcServer>();
app.MapControllers();

app.Run();
