using EcoTrace.Billing.Api.Grpc;
using EcoTrace.Billing.Api.ErrorHandling;
using EcoTrace.Billing.Domain.Interfaces.Repositories;
using EcoTrace.Billing.Domain.Interfaces.Services;
using EcoTrace.Billing.Domain.UseCases.Services;
using EcoTrace.Billing.Infrastructure.Data;
using EcoTrace.Billing.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionHandlingInterceptor>();
});
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

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
