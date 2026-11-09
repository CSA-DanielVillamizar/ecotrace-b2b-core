using EcoTrace.Mobile.Core.Cola;
using Microsoft.Data.Sqlite;

namespace EcoTrace.Mobile.Tests;

/// <summary>La cola local del ADR 0005: se guarda primero, se conserva, se ordena y nunca pierde una acción.</summary>
public sealed class ColaLocalTests : IDisposable
{
    private readonly BaseTemporal _base = new();

    public void Dispose() => _base.Dispose();

    [Fact]
    public async Task Encolar_guarda_la_accion_como_PendienteSync_con_un_OperationId_nacido_en_el_dispositivo()
    {
        var cola = _base.Abrir();

        var primera = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", """{"cargaId":"c1"}""");
        var segunda = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", """{"cargaId":"c1"}""");

        Assert.Equal(EstadoOperacion.PendienteSync, primera.Estado);
        Assert.NotEqual(Guid.Empty, primera.OperationId);
        // Dos acciones iguales son dos acciones: la identidad la da el OperationId, no el contenido.
        Assert.NotEqual(primera.OperationId, segunda.OperationId);

        var guardada = await cola.ObtenerAsync(primera.OperationId);
        Assert.Equal("Cargo", guardada!.Modulo);
        Assert.Equal("""{"cargaId":"c1"}""", guardada.Payload);
        Assert.Equal(0, guardada.Intentos);
    }

    [Fact]
    public async Task La_cola_sobrevive_a_cerrar_y_abrir_la_app()
    {
        var antes = await _base.Abrir().EncolarAsync("Fleet", "ReportarNovedad", "{}");

        var despues = await _base.Abrir().ListarAsync();

        Assert.Equal(antes.OperationId, Assert.Single(despues).OperationId);
    }

    [Fact]
    public async Task Se_lista_en_el_orden_en_que_se_encolo()
    {
        var cola = _base.Abrir();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await cola.EncolarAsync("Cargo", "ConfirmarEntrega", $"{{\"n\":{i}}}")).OperationId);
        }

        Assert.Equal(ids, (await cola.ListarAsync(EstadoOperacion.PendienteSync)).Select(o => o.OperationId));
    }

    [Fact]
    public async Task Filtra_por_estado_y_por_modulo()
    {
        var cola = _base.Abrir();
        var cargo = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        await cola.EncolarAsync("Billing", "RegistrarEvidencia", "{}");
        await cola.MarcarSincronizandoAsync(cargo.OperationId);

        Assert.Single(await cola.ListarAsync(modulo: "Billing"));
        Assert.Single(await cola.ListarAsync(EstadoOperacion.Sincronizando));
        Assert.Equal(1, await cola.ContarAsync(EstadoOperacion.PendienteSync));
    }

    [Fact]
    public async Task Un_ciclo_completo_pasa_por_Sincronizando_y_termina_en_Sincronizado()
    {
        var cola = _base.Abrir();
        var operacion = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");

        await cola.MarcarSincronizandoAsync(operacion.OperationId);
        await cola.MarcarSincronizadaAsync(operacion.OperationId);

        var final = await cola.ObtenerAsync(operacion.OperationId);
        Assert.Equal(EstadoOperacion.Sincronizado, final!.Estado);
        Assert.Equal(1, final.Intentos);
    }

    [Fact]
    public async Task Devolver_a_pendiente_cuenta_el_intento_y_guarda_el_motivo_sin_perder_la_accion()
    {
        var cola = _base.Abrir();
        var operacion = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");

        await cola.MarcarSincronizandoAsync(operacion.OperationId);
        await cola.DevolverAPendienteAsync(operacion.OperationId, "Sin respuesta del servidor.");
        await cola.MarcarSincronizandoAsync(operacion.OperationId);
        await cola.DevolverAPendienteAsync(operacion.OperationId, "Sin respuesta del servidor.");

        var actual = await cola.ObtenerAsync(operacion.OperationId);
        Assert.Equal(EstadoOperacion.PendienteSync, actual!.Estado);
        Assert.Equal(2, actual.Intentos);
        Assert.Equal("Sin respuesta del servidor.", actual.MotivoRechazo);
    }

    [Fact]
    public async Task Rechazar_conserva_la_accion_con_su_tipo_de_conflicto_y_su_motivo()
    {
        var cola = _base.Abrir();
        var operacion = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", """{"cargaId":"c1"}""");

        await cola.MarcarSincronizandoAsync(operacion.OperationId);
        await cola.RechazarAsync(operacion.OperationId, TipoConflicto.B, "La carga fue reasignada a otro conductor.");

        var rechazada = await cola.ObtenerAsync(operacion.OperationId);
        Assert.Equal(EstadoOperacion.Rechazado, rechazada!.Estado);
        Assert.Equal(TipoConflicto.B, rechazada.TipoConflicto);
        Assert.Equal("La carga fue reasignada a otro conductor.", rechazada.MotivoRechazo);
        Assert.Equal("""{"cargaId":"c1"}""", rechazada.Payload);
    }

    [Fact]
    public async Task Una_accion_no_puede_saltarse_el_ciclo_ni_salir_de_un_estado_final()
    {
        var cola = _base.Abrir();
        var operacion = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");

        // No se puede marcar como enviada una acción que nunca empezó a enviarse.
        await Assert.ThrowsAsync<InvalidOperationException>(() => cola.MarcarSincronizadaAsync(operacion.OperationId));

        await cola.MarcarSincronizandoAsync(operacion.OperationId);
        await cola.RechazarAsync(operacion.OperationId, TipoConflicto.B, "Rechazada.");

        // Una rechazada no vuelve a la cola por la puerta de atrás.
        await Assert.ThrowsAsync<InvalidOperationException>(() => cola.MarcarSincronizandoAsync(operacion.OperationId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cola.DevolverAPendienteAsync(operacion.OperationId, null));
    }

    [Fact]
    public async Task Lo_que_quedo_Sincronizando_porque_la_app_se_cerro_vuelve_a_PendienteSync()
    {
        var cola = _base.Abrir();
        var operacion = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        await cola.MarcarSincronizandoAsync(operacion.OperationId);

        // La app se cerró a mitad de un envío. Al abrir otra vez:
        var recuperadas = await _base.Abrir().RecuperarInterrumpidasAsync();

        Assert.Equal(1, recuperadas);
        var actual = await cola.ObtenerAsync(operacion.OperationId);
        Assert.Equal(EstadoOperacion.PendienteSync, actual!.Estado);
        Assert.Equal(operacion.OperationId, actual.OperationId);
    }

    [Fact]
    public async Task Un_conflicto_A_se_corrige_con_una_accion_nueva_y_la_rechazada_queda_para_auditoria()
    {
        var cola = _base.Abrir();
        var original = await cola.EncolarAsync("Identity", "ActualizarPerfil", """{"telefono":"123"}""");
        await cola.MarcarSincronizandoAsync(original.OperationId);
        await cola.RechazarAsync(original.OperationId, TipoConflicto.A, "El teléfono no tiene un formato válido.");

        var corregida = await cola.ReintentarCorregidaAsync(original.OperationId, """{"telefono":"3001234567"}""");

        Assert.NotEqual(original.OperationId, corregida.OperationId);
        Assert.Equal(EstadoOperacion.PendienteSync, corregida.Estado);
        Assert.Equal("ActualizarPerfil", corregida.Tipo);
        Assert.Equal(EstadoOperacion.Rechazado, (await cola.ObtenerAsync(original.OperationId))!.Estado);
    }

    [Theory]
    [InlineData(TipoConflicto.B)]
    [InlineData(TipoConflicto.C)]
    public async Task Los_conflictos_B_y_C_no_se_reintentan_corrigiendo(TipoConflicto tipo)
    {
        var cola = _base.Abrir();
        var operacion = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        await cola.MarcarSincronizandoAsync(operacion.OperationId);
        await cola.RechazarAsync(operacion.OperationId, tipo, "Rechazada.");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cola.ReintentarCorregidaAsync(operacion.OperationId, "{}"));
    }

    [Fact]
    public async Task Cada_cambio_en_la_cola_avisa_para_refrescar_la_pantalla()
    {
        var cola = _base.Abrir();
        var avisos = 0;
        cola.Cambio += (_, _) => avisos++;

        var operacion = await cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        await cola.MarcarSincronizandoAsync(operacion.OperationId);

        Assert.Equal(2, avisos);
    }

    [Fact]
    public async Task Guarda_y_devuelve_el_ultimo_contacto_con_el_servidor()
    {
        var cola = _base.Abrir();
        Assert.Null(await cola.UltimaSincronizacionAsync());

        var momento = new DateTime(2026, 10, 12, 15, 30, 0, DateTimeKind.Utc);
        await cola.RegistrarSincronizacionAsync(momento);

        Assert.Equal(momento, await _base.Abrir().UltimaSincronizacionAsync());
    }

    [Fact]
    public async Task La_tabla_tiene_solo_los_campos_del_contrato_y_ninguno_para_tokens_ni_contrasenas()
    {
        await _base.Abrir().EncolarAsync("Cargo", "ConfirmarEntrega", "{}");

        await using var conexion = new SqliteConnection($"Data Source={_base.Ruta};Pooling=false");
        await conexion.OpenAsync();
        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT name FROM pragma_table_info('ColaOperaciones') ORDER BY cid";
        var columnas = new List<string>();
        await using (var lector = await comando.ExecuteReaderAsync())
        {
            while (await lector.ReadAsync())
            {
                columnas.Add(lector.GetString(0));
            }
        }

        Assert.Equal(
            ["Secuencia", "OperationId", "Modulo", "Tipo", "Payload", "Estado", "TipoConflicto", "MotivoRechazo", "Intentos", "CreadaEnUtc", "ActualizadaEnUtc"],
            columnas);
    }

    [Fact]
    public async Task La_interfaz_de_la_cola_no_ofrece_forma_de_borrar_una_accion()
    {
        await Task.CompletedTask;
        var metodos = typeof(IColaLocal).GetMethods().Select(m => m.Name);

        Assert.DoesNotContain(metodos, n => n.Contains("Borrar", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Eliminar", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Limpiar", StringComparison.OrdinalIgnoreCase));
    }
}
