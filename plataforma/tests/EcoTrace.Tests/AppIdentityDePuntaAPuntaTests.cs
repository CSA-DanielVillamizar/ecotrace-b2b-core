using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.App.Modulos.Identity.Logica;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// El módulo Identity de la app contra Identity real, de punta a punta (checkpoint 2 del Trabajo 3): login, una
/// acción encolada sin señal que se sincroniza al volver la señal, un reintento que no duplica el efecto, los
/// conflictos A y B, y la renovación del token sin intervención del conductor.
/// </summary>
public sealed class AppIdentityDePuntaAPuntaTests : IAsyncLifetime
{
    private const string Password = "Conductor-2026";

    private readonly ApiFactory<IdentityApiMarker> _identity = new();
    private readonly List<TelefonoDePrueba> _telefonos = [];
    private HttpClient _admin = null!;
    private Guid _organizacion;
    private string _email = null!;

    public async Task InitializeAsync()
    {
        _admin = await Acceso.ClienteAdministradorAsync(_identity.CreateClient);
        _organizacion = (await _admin.PostearAsync("/api/tenants", new { nombre = "Transportes del Cauca", tenantType = "Transportista" })).Id("tenantId");
        _email = $"carlos.{Guid.NewGuid():N}@transportescauca.co";
        await _admin.CrearUsuarioAsync(_organizacion, "Conductor", _email, Password);
    }

    public async Task DisposeAsync()
    {
        _telefonos.ForEach(t => t.Dispose());
        _admin.Dispose();
        await _identity.DisposeAsync();
    }

    [Fact]
    public async Task Login_cambio_sin_senal_y_sincronizacion_al_volver_la_senal()
    {
        var telefono = await TelefonoConSesionAsync();
        Assert.True(await telefono.Perfil.RefrescarAsync());

        // Modo avión: el cambio se guarda en la cola y la pantalla lo muestra de inmediato.
        telefono.Conectividad.HayConexion = false;
        var cambio = await telefono.Perfil.CambiarPerfilAsync("Carlos Ruiz Díaz", "+57 300 123 4567");
        var sinSenal = await telefono.Motor.SincronizarAsync();
        var vistaSinSenal = await telefono.Perfil.ObtenerVistaAsync();

        Assert.Equal(ParadaSincronizacion.SinConexion, sinSenal.Parada);
        Assert.Equal(EstadoOperacion.PendienteSync, (await telefono.Cola.ObtenerAsync(cambio.OperationId))!.Estado);
        Assert.Equal("Carlos Ruiz Díaz", vistaSinSenal.Nombre);
        Assert.Equal("Transportes del Cauca", vistaSinSenal.Organizacion);
        Assert.Contains("Se envía cuando haya señal", vistaSinSenal.Aviso);

        // Vuelve la señal: el motor la envía con el token de la sesión y el OperationId como Idempotency-Key.
        telefono.Conectividad.HayConexion = true;
        var conSenal = await telefono.Motor.SincronizarAsync();
        Assert.True(await telefono.Perfil.RefrescarAsync());
        var vista = await telefono.Perfil.ObtenerVistaAsync();

        Assert.Equal(1, conSenal.Sincronizadas);
        Assert.Equal(EstadoOperacion.Sincronizado, (await telefono.Cola.ObtenerAsync(cambio.OperationId))!.Estado);
        Assert.Equal("+573001234567", vista.Telefono);
        Assert.Null(vista.Aviso);
        Assert.Equal("+573001234567", (await PerfilEnElServidorAsync()).Texto("telefono"));
    }

    [Fact]
    public async Task Si_se_pierde_la_respuesta_el_reenvio_no_repite_el_cambio_ni_pisa_uno_mas_nuevo()
    {
        var telefono = await TelefonoConSesionAsync();
        var cambio = await telefono.Perfil.CambiarPerfilAsync("Carlos Ruiz", "3001111111");

        // El servidor aplica el cambio, pero la respuesta no llega al teléfono.
        telefono.Red.PerderProximaRespuesta();
        var perdida = await telefono.Motor.SincronizarAsync();
        Assert.Equal(ParadaSincronizacion.ErrorTransitorio, perdida.Parada);
        Assert.Equal(EstadoOperacion.PendienteSync, (await telefono.Cola.ObtenerAsync(cambio.OperationId))!.Estado);

        // Mientras tanto, el conductor cambia el teléfono desde otro dispositivo.
        var otro = await TelefonoConSesionAsync();
        await otro.Perfil.CambiarPerfilAsync("Carlos Ruiz", "3002222222");
        Assert.Equal(1, (await otro.Motor.SincronizarAsync()).Sincronizadas);

        // El primer teléfono reenvía con la misma clave: el servidor responde lo de la primera vez y no lo aplica de nuevo.
        var reenvio = await telefono.Motor.SincronizarAsync();

        Assert.Equal(1, reenvio.Sincronizadas);
        Assert.Equal(EstadoOperacion.Sincronizado, (await telefono.Cola.ObtenerAsync(cambio.OperationId))!.Estado);
        Assert.Equal("3002222222", (await PerfilEnElServidorAsync()).Texto("telefono"));
    }

    [Fact]
    public async Task Un_telefono_invalido_vuelve_como_conflicto_A_y_se_corrige_desde_la_app()
    {
        var telefono = await TelefonoConSesionAsync();
        var mala = await telefono.Perfil.CambiarPerfilAsync("Carlos Ruiz", "12ab");

        var envio = await telefono.Motor.SincronizarAsync();
        var vista = await telefono.Perfil.ObtenerVistaAsync();

        Assert.Equal(1, envio.Rechazadas);
        var rechazada = (await telefono.Cola.ObtenerAsync(mala.OperationId))!;
        Assert.Equal(EstadoOperacion.Rechazado, rechazada.Estado);
        Assert.Equal(TipoConflicto.A, rechazada.TipoConflicto);
        Assert.Contains("teléfono", rechazada.MotivoRechazo);
        Assert.True(vista.PuedeCorregir);

        var corregida = await telefono.Perfil.CorregirAsync(mala.OperationId, "Carlos Ruiz", "3001234567");
        Assert.Equal(1, (await telefono.Motor.SincronizarAsync()).Sincronizadas);

        Assert.Equal(EstadoOperacion.Sincronizado, (await telefono.Cola.ObtenerAsync(corregida.OperationId))!.Estado);
        Assert.Equal(EstadoOperacion.Rechazado, (await telefono.Cola.ObtenerAsync(mala.OperationId))!.Estado);
        Assert.Equal("3001234567", (await PerfilEnElServidorAsync()).Texto("telefono"));
    }

    [Fact]
    public async Task Si_suspenden_la_organizacion_mientras_no_hay_senal_el_cambio_vuelve_como_conflicto_B()
    {
        var telefono = await TelefonoConSesionAsync();
        telefono.Conectividad.HayConexion = false;
        var cambio = await telefono.Perfil.CambiarPerfilAsync("Carlos Ruiz", "3001234567");

        await _admin.PostearAsync($"/api/tenants/{_organizacion}/suspension");
        telefono.Conectividad.HayConexion = true;
        var envio = await telefono.Motor.SincronizarAsync();
        var vista = await telefono.Perfil.ObtenerVistaAsync();

        Assert.Equal(1, envio.Rechazadas);
        var rechazada = (await telefono.Cola.ObtenerAsync(cambio.OperationId))!;
        Assert.Equal(TipoConflicto.B, rechazada.TipoConflicto);
        Assert.Contains("suspendida", rechazada.MotivoRechazo);
        Assert.False(vista.PuedeCorregir);
        Assert.Equal(TipoConflicto.B, vista.TipoDeRechazo);
    }

    [Fact]
    public async Task Con_el_token_vencido_el_motor_lo_renueva_solo_y_envia()
    {
        var telefono = await TelefonoConSesionAsync();
        var antes = (await telefono.Sesion.EstadoAsync()).UltimoContactoUtc;
        await telefono.Perfil.CambiarPerfilAsync("Carlos Ruiz", "3007777777");

        telefono.Reloj.Adelantar(TimeSpan.FromMinutes(20));
        var envio = await telefono.Motor.SincronizarAsync();

        Assert.Equal(1, envio.Sincronizadas);
        Assert.True((await telefono.Sesion.EstadoAsync()).UltimoContactoUtc > antes);
        Assert.Equal("3007777777", (await PerfilEnElServidorAsync()).Texto("telefono"));
    }

    [Fact]
    public async Task Cerrar_sesion_en_otros_dispositivos_deja_sin_sesion_al_otro_telefono_al_renovar()
    {
        var principal = await TelefonoConSesionAsync();
        var otro = await TelefonoConSesionAsync();

        await principal.Perfil.CerrarOtrasSesionesAsync();
        Assert.Equal(1, (await principal.Motor.SincronizarAsync()).Sincronizadas);

        // El otro teléfono sigue con su access token hasta que vence; al renovar, Identity lo rechaza.
        otro.Reloj.Adelantar(TimeSpan.FromMinutes(20));
        principal.Reloj.Adelantar(TimeSpan.FromMinutes(20));

        Assert.Null(await otro.Sesion.ObtenerTokenVigenteAsync());
        var sesionDelOtro = await otro.Sesion.EstadoAsync();
        Assert.False(sesionDelOtro.Iniciada);
        Assert.Contains("otro dispositivo", sesionDelOtro.MotivoDeCierre);
        Assert.NotNull(await principal.Sesion.ObtenerTokenVigenteAsync());
    }

    [Fact]
    public async Task Sin_sesion_las_acciones_esperan_y_salen_despues_del_login()
    {
        var telefono = NuevoTelefono();
        var cambio = await telefono.Perfil.CambiarPerfilAsync("Carlos Ruiz", "3008888888");

        Assert.Equal(ParadaSincronizacion.SinSesion, (await telefono.Motor.SincronizarAsync()).Parada);

        Assert.True((await telefono.Sesion.IniciarSesionAsync(_email, Password)).Exitosa);
        Assert.Equal(1, (await telefono.Motor.SincronizarAsync()).Sincronizadas);
        Assert.Equal(EstadoOperacion.Sincronizado, (await telefono.Cola.ObtenerAsync(cambio.OperationId))!.Estado);
    }

    [Fact]
    public async Task En_un_telefono_compartido_las_acciones_de_un_conductor_nunca_salen_con_el_token_de_otro()
    {
        var marta = $"marta.{Guid.NewGuid():N}@transportescauca.co";
        await _admin.CrearUsuarioAsync(_organizacion, "Conductor", marta, Password);
        var telefono = await TelefonoConSesionAsync();

        // Carlos cambia su teléfono sin señal y cierra sesión con la acción pendiente.
        telefono.Conectividad.HayConexion = false;
        var cambio = await telefono.Perfil.CambiarPerfilAsync("Carlos Ruiz", "3006666666");
        telefono.Conectividad.HayConexion = true;
        await telefono.Sesion.CerrarSesionAsync();

        // Marta no puede entrar mientras la acción de Carlos siga en la cola.
        var deMarta = await telefono.Sesion.IniciarSesionAsync(marta, Password);
        Assert.Equal(EstadoAutenticacion.AccionesDeOtraCuenta, deMarta.Estado);
        Assert.Equal(ParadaSincronizacion.SinSesion, (await telefono.Motor.SincronizarAsync()).Parada);

        // Carlos vuelve: su acción sale con su token y cae en su perfil, no en el de Marta.
        Assert.True((await telefono.Sesion.IniciarSesionAsync(_email, Password)).Exitosa);
        Assert.Equal(1, (await telefono.Motor.SincronizarAsync()).Sincronizadas);
        Assert.Equal(EstadoOperacion.Sincronizado, (await telefono.Cola.ObtenerAsync(cambio.OperationId))!.Estado);
        Assert.Equal("3006666666", (await PerfilEnElServidorAsync()).Texto("telefono"));

        using var cliente = _identity.CreateClient().ConToken(await _identity.CreateClient().TokenDeAsync(marta, Password));
        Assert.Equal(JsonValueKind.Null, (await cliente.ConsultarAsync("/api/perfil")).Propiedad("telefono").ValueKind);

        // Con la cola ya enviada, Marta sí entra.
        await telefono.Sesion.CerrarSesionAsync();
        Assert.True((await telefono.Sesion.IniciarSesionAsync(marta, Password)).Exitosa);
    }

    [Fact]
    public async Task Si_suspenden_la_organizacion_del_dueno_de_la_cola_otra_cuenta_entra_confirmando_sin_enviar_lo_ajeno()
    {
        var otraOrganizacion = (await _admin.PostearAsync("/api/tenants", new { nombre = "Transportes del Sur", tenantType = "Transportista" })).Id("tenantId");
        var marta = $"marta.{Guid.NewGuid():N}@transportesdelsur.co";
        await _admin.CrearUsuarioAsync(otraOrganizacion, "Conductor", marta, Password);
        var telefono = await TelefonoConSesionAsync();

        telefono.Conectividad.HayConexion = false;
        var cambio = await telefono.Perfil.CambiarPerfilAsync("Carlos Ruiz", "3006666666");
        telefono.Conectividad.HayConexion = true;
        await telefono.Sesion.CerrarSesionAsync();
        await _admin.PostearAsync($"/api/tenants/{_organizacion}/suspension");

        // Carlos ya no puede entrar, y Marta solo entra si lo confirma.
        Assert.Equal(EstadoAutenticacion.OrganizacionSuspendida, (await telefono.Sesion.IniciarSesionAsync(_email, Password)).Estado);
        Assert.Equal(EstadoAutenticacion.AccionesDeOtraCuenta, (await telefono.Sesion.IniciarSesionAsync(marta, Password)).Estado);
        Assert.True((await telefono.Sesion.IniciarSesionAsync(marta, Password, rechazarAccionesDeOtraCuenta: true)).Exitosa);

        // La acción de Carlos queda rechazada con su motivo y nunca sale con el token de Marta.
        var envio = await telefono.Motor.SincronizarAsync();
        var deCarlos = (await telefono.Cola.ObtenerAsync(cambio.OperationId))!;

        Assert.Equal(0, envio.Sincronizadas);
        Assert.Equal(EstadoOperacion.Rechazado, deCarlos.Estado);
        Assert.Equal(TipoConflicto.B, deCarlos.TipoConflicto);
        using var cliente = _identity.CreateClient().ConToken(await _identity.CreateClient().TokenDeAsync(marta, Password));
        Assert.Equal(JsonValueKind.Null, (await cliente.ConsultarAsync("/api/perfil")).Propiedad("telefono").ValueKind);
    }

    private TelefonoDePrueba NuevoTelefono()
    {
        var telefono = new TelefonoDePrueba(() => _identity.Server.CreateHandler());
        _telefonos.Add(telefono);
        return telefono;
    }

    private async Task<TelefonoDePrueba> TelefonoConSesionAsync()
    {
        var telefono = NuevoTelefono();
        var login = await telefono.Sesion.IniciarSesionAsync(_email, Password);
        Assert.True(login.Exitosa, login.Mensaje);
        return telefono;
    }

    private async Task<Respuesta> PerfilEnElServidorAsync()
    {
        using var cliente = _identity.CreateClient().ConToken(await _identity.CreateClient().TokenDeAsync(_email, Password));
        return await cliente.ConsultarAsync("/api/perfil");
    }
}
