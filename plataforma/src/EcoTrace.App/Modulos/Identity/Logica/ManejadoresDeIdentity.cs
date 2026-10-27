using System.Text;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Identity.Logica;

/// <summary>Nombres de las acciones que encola el módulo. Son los que guarda la cola y los que ve el conductor.</summary>
public static class AccionesDeIdentity
{
    public const string Modulo = "Identity";
    public const string ActualizarPerfil = "ActualizarPerfil";
    public const string CerrarOtrasSesiones = "CerrarOtrasSesiones";
}

/// <summary>Lo que se guarda en la cola al cambiar el perfil. Se manda completo: un teléfono vacío lo borra.</summary>
public sealed record DatosDePerfil(string Nombre, string? Telefono);

/// <summary>Cambio de perfil: <c>PUT /api/perfil</c>. El motor agrega el token y el Idempotency-Key.</summary>
public sealed class ActualizarPerfilHandler(ConfiguracionServicios servicios) : IManejadorOperacion
{
    public string Modulo => AccionesDeIdentity.Modulo;

    public string Tipo => AccionesDeIdentity.ActualizarPerfil;

    public HttpRequestMessage Construir(OperacionPendiente operacion) =>
        new(HttpMethod.Put, new Uri(servicios.Identity, "/api/perfil"))
        {
            Content = new StringContent(operacion.Payload, Encoding.UTF8, "application/json")
        };
}

/// <summary>Cierre de sesión en los otros dispositivos: <c>POST /api/perfil/cierre-otras-sesiones</c>.</summary>
public sealed class CerrarOtrasSesionesHandler(ConfiguracionServicios servicios) : IManejadorOperacion
{
    public string Modulo => AccionesDeIdentity.Modulo;

    public string Tipo => AccionesDeIdentity.CerrarOtrasSesiones;

    public HttpRequestMessage Construir(OperacionPendiente operacion) =>
        new(HttpMethod.Post, new Uri(servicios.Identity, "/api/perfil/cierre-otras-sesiones"));
}
