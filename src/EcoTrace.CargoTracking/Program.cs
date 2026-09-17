using Microsoft.EntityFrameworkCore;
using EcoTrace.CargoTracking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Evita el error "possible object cycle detected" cuando una
        // entidad tiene navegación en ambas direcciones (Carga -> sus
        // Asignaciones -> de vuelta a Carga -> ...). En vez de fallar,
        // simplemente omite la referencia repetida al serializar.
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// Registramos nuestro DbContext, indicándole que use PostgreSQL
// (Npgsql) con la cadena de conexión definida en appsettings.json.
builder.Services.AddDbContext<CargoTrackingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();