using EcoTrace.FleetManagement.Api.Grpc;
using EcoTrace.FleetManagement.Api.ErrorHandling;
using EcoTrace.FleetManagement.Domain.Repositories;
using EcoTrace.FleetManagement.Domain.Services;
using EcoTrace.FleetManagement.Infrastructure.Data;
using EcoTrace.FleetManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionHandlingInterceptor>();
});
builder.Services.AddControllers();

builder.Services.AddDbContext<FleetDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("FleetDatabase")));

builder.Services.AddScoped<IDriverRepository, DriverRepository>();
builder.Services.AddScoped<IVehicleRepository, VehicleRepository>();

builder.Services.AddScoped<IDriverService, DriverService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapGrpcService<FleetManagementGrpcServer>();
app.MapControllers();

app.Run();
