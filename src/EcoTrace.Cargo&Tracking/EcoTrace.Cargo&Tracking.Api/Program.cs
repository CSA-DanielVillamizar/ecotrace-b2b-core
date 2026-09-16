using EcoTrace.Cargo_Tracking.Api.Grpc;
using EcoTrace.Cargo_Tracking.Api.ErrorHandling;
using EcoTrace.Cargo_Tracking.Domain.Repositories;
using EcoTrace.Cargo_Tracking.Domain.Services;
using EcoTrace.Cargo_Tracking.Infrastructure.Data;
using EcoTrace.Cargo_Tracking.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionHandlingInterceptor>();
});
builder.Services.AddControllers();

builder.Services.AddDbContext<CargoTrackingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CargoTrackingDatabase")));

builder.Services.AddScoped<ICargoRepository, CargoRepository>();
builder.Services.AddScoped<ICargoAssignmentRepository, CargoAssignmentRepository>();
builder.Services.AddScoped<ITrackingRepository, TrackingRepository>();

builder.Services.AddScoped<ICargoService, CargoService>();
builder.Services.AddScoped<ICargoAssignmentService, CargoAssignmentService>();
builder.Services.AddScoped<ITrackingService, TrackingService>();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapGrpcService<CargoTrackingGrpcServer>();
app.MapControllers();

app.Run();
