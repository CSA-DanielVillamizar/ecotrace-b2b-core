using System.Net;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Tests;

/// <summary>El motor de sincronización: orden, idempotencia, reintentos y conflictos (ADR 0005).</summary>
public sealed class MotorSincronizacionTests : IDisposable
{
    private readonly Entorno _e = new("ConfirmarEntrega", "CambiarEstado");

    public void Dispose() => _e.Dispose();

    [Fact]
    public async Task Envia_en_el_orden_en_que_se_encolo_con_el_token_y_el_OperationId_como_Idempotency_Key()
    {
        var primera = await _e.Cola.EncolarAsync("Cargo", "CambiarEstado", """{"orden":1}""");
        var segunda = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", """{"orden":2}""");
        var tercera = await _e.Cola.EncolarAsync("Cargo", "CambiarEstado", """{"orden":3}""");

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(new ResultadoSincronizacion(3, 0, 0, ParadaSincronizacion.Ninguna), resultado);
        Assert.Equal(
            [primera.OperationId.ToString("D"), segunda.OperationId.ToString("D"), tercera.OperationId.ToString("D")],
            _e.Servidor.Recibidas.Select(r => r.IdempotencyKey));
        Assert.All(_e.Servidor.Recibidas, r => Assert.Equal("Bearer token-vigente", r.Authorization));
        Assert.Equal(["""{"orden":1}""", """{"orden":2}""", """{"orden":3}"""], _e.Servidor.Recibidas.Select(r => r.Cuerpo));
        Assert.Equal(3, await _e.Cola.ContarAsync(EstadoOperacion.Sincronizado));
    }

    [Fact]
    public async Task El_correlation_id_sale_del_OperationId_para_seguir_la_accion_en_los_registros_del_servidor()
    {
        var operacion = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");

        await _e.Motor.SincronizarAsync();

        Assert.Equal(operacion.OperationId.ToString("N"), Assert.Single(_e.Servidor.Recibidas).CorrelationId);
    }

    [Fact]
    public async Task Un_reintento_tras_un_corte_reenvia_la_misma_clave_y_un_200_cuenta_como_exito_sin_repetir_el_efecto()
    {
        var operacion = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");

        // Primer envío: el servidor lo procesa pero la respuesta se pierde en el camino.
        _e.Servidor.Responder = _ => throw new HttpRequestException("Se cortó la red.");
        var corte = await _e.Motor.SincronizarAsync();
        Assert.Equal(ParadaSincronizacion.ErrorTransitorio, corte.Parada);

        // Segundo envío: el servidor ya la conoce y responde 200 con el resultado anterior.
        _e.Servidor.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        var reintento = await _e.Motor.SincronizarAsync();

        Assert.Equal(1, reintento.Sincronizadas);
        Assert.Equal(2, _e.Servidor.Recibidas.Count);
        Assert.Equal(_e.Servidor.Recibidas[0].IdempotencyKey, _e.Servidor.Recibidas[1].IdempotencyKey);
        Assert.Equal(operacion.OperationId.ToString("D"), _e.Servidor.Recibidas[1].IdempotencyKey);
        var final = await _e.Cola.ObtenerAsync(operacion.OperationId);
        Assert.Equal(EstadoOperacion.Sincronizado, final!.Estado);
        Assert.Equal(2, final.Intentos);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Una_falla_transitoria_devuelve_la_accion_a_la_cola_y_detiene_la_sincronizacion_para_no_enviar_fuera_de_orden(
        HttpStatusCode estado)
    {
        var primera = await _e.Cola.EncolarAsync("Cargo", "CambiarEstado", "{}");
        var segunda = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Servidor.Responder = _ => Task.FromResult(new HttpResponseMessage(estado));

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(new ResultadoSincronizacion(0, 0, 2, ParadaSincronizacion.ErrorTransitorio), resultado);
        Assert.Single(_e.Servidor.Recibidas);
        Assert.Equal(EstadoOperacion.PendienteSync, (await _e.Cola.ObtenerAsync(primera.OperationId))!.Estado);
        Assert.Equal(0, (await _e.Cola.ObtenerAsync(segunda.OperationId))!.Intentos);
    }

    [Fact]
    public async Task Una_excepcion_de_red_no_pierde_la_accion()
    {
        var operacion = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Servidor.Responder = _ => throw new HttpRequestException("Sin ruta al host.");

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(ParadaSincronizacion.ErrorTransitorio, resultado.Parada);
        var actual = await _e.Cola.ObtenerAsync(operacion.OperationId);
        Assert.Equal(EstadoOperacion.PendienteSync, actual!.Estado);
        Assert.Equal(1, actual.Intentos);
    }

    [Fact]
    public async Task Un_servidor_que_no_responde_a_tiempo_es_una_falla_transitoria()
    {
        var operacion = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Servidor.Responder = async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return new HttpResponseMessage(HttpStatusCode.Created);
        };
        var motor = new MotorSincronizacion(_e.Cola, _e.Registro, _e.Sesion, _e.Conectividad, _e.Http)
        {
            TiempoMaximoPorEnvio = TimeSpan.FromMilliseconds(100)
        };

        var resultado = await motor.SincronizarAsync();

        Assert.Equal(ParadaSincronizacion.ErrorTransitorio, resultado.Parada);
        Assert.Equal(EstadoOperacion.PendienteSync, (await _e.Cola.ObtenerAsync(operacion.OperationId))!.Estado);
    }

    [Theory]
    [InlineData("A", TipoConflicto.A)]
    [InlineData("B", TipoConflicto.B)]
    [InlineData("C", TipoConflicto.C)]
    public async Task Un_rechazo_conserva_la_accion_con_el_tipo_de_conflicto_que_dijo_el_servidor(string tipo, TipoConflicto esperado)
    {
        var operacion = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Servidor.Responder = _ => Task.FromResult(
            ServidorFalso.Problema(HttpStatusCode.Conflict, tipo, "Motivo del servidor."));

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(new ResultadoSincronizacion(0, 1, 0, ParadaSincronizacion.Ninguna), resultado);
        var rechazada = await _e.Cola.ObtenerAsync(operacion.OperationId);
        Assert.Equal(EstadoOperacion.Rechazado, rechazada!.Estado);
        Assert.Equal(esperado, rechazada.TipoConflicto);
        Assert.Equal("Motivo del servidor.", rechazada.MotivoRechazo);
    }

    [Fact]
    public async Task Un_rechazo_no_bloquea_las_acciones_que_siguen_en_la_cola()
    {
        var rechazada = await _e.Cola.EncolarAsync("Cargo", "CambiarEstado", "{}");
        var buena = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Servidor.Responder = s => Task.FromResult(s.Direccion.AbsolutePath.EndsWith("CambiarEstado", StringComparison.Ordinal)
            ? ServidorFalso.Problema(HttpStatusCode.Conflict, "B", "La carga fue reasignada.")
            : new HttpResponseMessage(HttpStatusCode.Created));

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(new ResultadoSincronizacion(1, 1, 0, ParadaSincronizacion.Ninguna), resultado);
        Assert.Equal(EstadoOperacion.Rechazado, (await _e.Cola.ObtenerAsync(rechazada.OperationId))!.Estado);
        Assert.Equal(EstadoOperacion.Sincronizado, (await _e.Cola.ObtenerAsync(buena.OperationId))!.Estado);
    }

    [Fact]
    public async Task Un_rechazo_no_se_reintenta_en_la_siguiente_sincronizacion()
    {
        await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Servidor.Responder = _ => Task.FromResult(ServidorFalso.Problema(HttpStatusCode.Conflict, "B", "Ya no aplica."));

        await _e.Motor.SincronizarAsync();
        await _e.Motor.SincronizarAsync();

        Assert.Single(_e.Servidor.Recibidas);
    }

    [Fact]
    public async Task Sin_token_vigente_no_se_envia_nada_y_las_acciones_siguen_pendientes()
    {
        var operacion = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Sesion.Token = null;

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(new ResultadoSincronizacion(0, 0, 1, ParadaSincronizacion.SinSesion), resultado);
        Assert.Empty(_e.Servidor.Recibidas);
        Assert.Equal(EstadoOperacion.PendienteSync, (await _e.Cola.ObtenerAsync(operacion.OperationId))!.Estado);
    }

    [Fact]
    public async Task Cuando_el_token_vuelve_a_ser_vigente_se_sincroniza_con_el_token_nuevo()
    {
        await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Sesion.Token = null;
        await _e.Motor.SincronizarAsync();

        _e.Sesion.Token = "token-renovado";
        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(1, resultado.Sincronizadas);
        Assert.Equal("Bearer token-renovado", Assert.Single(_e.Servidor.Recibidas).Authorization);
    }

    [Fact]
    public async Task Sin_conexion_no_se_intenta_enviar()
    {
        await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Conectividad.HayConexion = false;

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(new ResultadoSincronizacion(0, 0, 1, ParadaSincronizacion.SinConexion), resultado);
        Assert.Empty(_e.Servidor.Recibidas);
    }

    [Fact]
    public async Task Un_401_deja_la_accion_pendiente_avisa_a_la_sesion_y_detiene_la_sincronizacion()
    {
        var operacion = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        await _e.Cola.EncolarAsync("Cargo", "CambiarEstado", "{}");
        _e.Servidor.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(new ResultadoSincronizacion(0, 0, 2, ParadaSincronizacion.TokenRechazado), resultado);
        Assert.Equal(1, _e.Sesion.TokenRechazado);
        Assert.Single(_e.Servidor.Recibidas);
        Assert.Equal(EstadoOperacion.PendienteSync, (await _e.Cola.ObtenerAsync(operacion.OperationId))!.Estado);
    }

    [Fact]
    public async Task Una_accion_que_quedo_Sincronizando_porque_la_app_se_cerro_se_reenvia_con_el_mismo_OperationId()
    {
        var operacion = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        await _e.Cola.MarcarSincronizandoAsync(operacion.OperationId);

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(1, resultado.Sincronizadas);
        Assert.Equal(operacion.OperationId.ToString("D"), Assert.Single(_e.Servidor.Recibidas).IdempotencyKey);
    }

    [Fact]
    public async Task Una_accion_de_un_tipo_que_la_app_no_sabe_enviar_se_rechaza_con_motivo_y_no_se_pierde()
    {
        var desconocida = await _e.Cola.EncolarAsync("Cargo", "TipoQueNoExiste", "{}");
        var buena = await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");

        var resultado = await _e.Motor.SincronizarAsync();

        Assert.Equal(new ResultadoSincronizacion(1, 1, 0, ParadaSincronizacion.Ninguna), resultado);
        var rechazada = await _e.Cola.ObtenerAsync(desconocida.OperationId);
        Assert.Equal(EstadoOperacion.Rechazado, rechazada!.Estado);
        Assert.Contains("TipoQueNoExiste", rechazada.MotivoRechazo);
        Assert.Equal(EstadoOperacion.Sincronizado, (await _e.Cola.ObtenerAsync(buena.OperationId))!.Estado);
    }

    [Fact]
    public async Task Dos_sincronizaciones_a_la_vez_no_envian_la_misma_accion_dos_veces()
    {
        await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        var liberar = new TaskCompletionSource();
        _e.Servidor.Responder = async _ =>
        {
            await liberar.Task;
            return new HttpResponseMessage(HttpStatusCode.Created);
        };

        var primera = _e.Motor.SincronizarAsync();
        while (_e.Servidor.Recibidas.Count == 0)
        {
            await Task.Delay(10);
        }

        var segunda = await _e.Motor.SincronizarAsync();
        liberar.SetResult();
        await primera;

        Assert.Equal(ParadaSincronizacion.YaEnCurso, segunda.Parada);
        Assert.Single(_e.Servidor.Recibidas);
    }

    [Fact]
    public async Task Cualquier_respuesta_del_servidor_cuenta_como_ultimo_contacto_incluso_un_rechazo()
    {
        await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Servidor.Responder = _ => Task.FromResult(ServidorFalso.Problema(HttpStatusCode.Conflict, "B", "Ya no aplica."));
        Assert.Null(await _e.Cola.UltimaSincronizacionAsync());

        await _e.Motor.SincronizarAsync();

        Assert.NotNull(await _e.Cola.UltimaSincronizacionAsync());
    }

    [Fact]
    public async Task Una_falla_de_red_no_cuenta_como_ultimo_contacto()
    {
        await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Servidor.Responder = _ => throw new HttpRequestException("Sin red.");

        await _e.Motor.SincronizarAsync();

        Assert.Null(await _e.Cola.UltimaSincronizacionAsync());
    }

    [Fact]
    public async Task El_disparador_sincroniza_solo_cuando_vuelve_la_senal()
    {
        await _e.Cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        _e.Conectividad.HayConexion = false;
        using var disparador = new DisparadorDeSincronizacion(_e.Motor, _e.Conectividad) { Intervalo = TimeSpan.FromMinutes(10) };
        disparador.Iniciar();
        await Task.Delay(200);
        Assert.Empty(_e.Servidor.Recibidas);

        _e.Conectividad.HayConexion = true;

        for (var i = 0; i < 100 && _e.Servidor.Recibidas.Count == 0; i++)
        {
            await Task.Delay(50);
        }

        Assert.Single(_e.Servidor.Recibidas);
    }
}
