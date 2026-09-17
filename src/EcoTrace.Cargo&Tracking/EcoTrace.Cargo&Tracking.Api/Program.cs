using EcoTrace.Cargo_Tracking.Api.Grpc;
using EcoTrace.Cargo_Tracking.Api.ErrorHandling;
using EcoTrace.Cargo_Tracking.Domain.Interfaces.Repositories;
using EcoTrace.Cargo_Tracking.Domain.Interfaces.Services;
using EcoTrace.Cargo_Tracking.Domain.UseCases.Services;
using EcoTrace.Cargo_Tracking.Infrastructure.Data;
using EcoTrace.Cargo_Tracking.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionHandlingInterceptor>();
});
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

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
