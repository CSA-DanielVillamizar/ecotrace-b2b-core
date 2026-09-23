var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// La consola descubre las APIs leyendo este archivo, asi el mismo build sirve en local y en
// contenedores sin recompilar. Los valores salen de appsettings.json o de variables de entorno.
app.MapGet("/config.json", (IConfiguration configuracion) => Results.Json(new
{
    servicios = new
    {
        identity = configuracion["Servicios:Identity"] ?? "http://localhost:5101",
        fleet = configuracion["Servicios:FleetManagement"] ?? "http://localhost:5102",
        cargo = configuracion["Servicios:CargoTracking"] ?? "http://localhost:5103",
        billing = configuracion["Servicios:Billing"] ?? "http://localhost:5104"
    }
}));

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Es una consola de referencia que se edita a menudo: sin cache para ver cada cambio.
    OnPrepareResponse = contexto =>
        contexto.Context.Response.Headers.CacheControl = "no-cache, no-store"
});

app.Run();
