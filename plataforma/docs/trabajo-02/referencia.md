# Trabajo 2: la referencia, criterio por criterio

Este documento acompaña la implementación de referencia del [Trabajo 2](especificacion.md). No es la única forma de resolverlo; es una forma, con sus razones, para que cada escuadrón compare su entrega con algo concreto. Cada fila de la rúbrica del Issue #23 apunta al código que la cumple y a la prueba que la demuestra.

Las pruebas están en `tests/EcoTrace.Tests/`. Para correrlas todas:

```bash
dotnet test EcoTrace.sln
```

Para ver el Saga con sus propios ojos, `scripts/run-all.ps1` (o `.sh`) levanta los cuatro servicios con el modo de fallo simulado habilitado, y [verificacion.http](verificacion.http) recorre el camino feliz y la compensación.

## Comunicación real entre contextos (1.0)

Billing llama a Identity y a Fleet por HTTP con los contratos de la especificación, y Cargo & Tracking le entrega el evento a Billing por HTTP. Ningún servicio abre la base de otro. Las direcciones salen de `Servicios__<Nombre>__BaseUrl`.

| Dónde | Qué hace |
|---|---|
| `EcoTrace.Billing/Api/Saga/ServiciosExternos.cs` | Clientes HTTP de Billing hacia Identity y Fleet. Cada uno declara sus propios contratos. |
| `EcoTrace.CargoTracking/Api/Outbox/OutboxPublicador.cs` | Entrega el evento a `POST /api/eventos/entrega-confirmada` de Billing. |
| `EcoTrace.Billing/Api/Extensions/ApiDefaults.cs` y los de los otros tres | `X-Correlation-Id`: se lee, se genera si falta, se devuelve y se registra. |

Pruebas: `SagaLiberacionPagoTests.Camino_feliz_entregar_la_carga_libera_el_pago_y_los_recursos_a_traves_de_los_cuatro_contextos` levanta los cuatro servicios reales y recorre la cadena; `ContratosEntreServiciosTests.Cada_respuesta_devuelve_el_CorrelationId_y_respeta_el_que_envia_el_llamador`. La regla de no compartir proyectos la hace cumplir `ArquitecturaTests`.

## Los pasos del Saga (1.0)

`SagaLiberacionPago` (en `EcoTrace.Billing/Domain/`) es una entidad con cuatro pasos persistidos: `AutorizarPago`, `LiberarFondos`, `LiberarRecursos` y `RegistrarAuditoria`. El estado se guarda después de cada paso, así que un reinicio retoma donde quedó. `SagaEjecutor` los corre y `SagaDispatcherService` busca los que quedaron pendientes al arrancar. `GET /api/sagas` y `GET /api/sagas/{id}` los exponen.

Los estados son `EnCurso`, `Completada`, `Compensando`, `Compensada`, `Fallida` y `RequiereIntervencion`. Tiene un `Version` como token de concurrencia: dos ejecutores que avanzan el mismo Saga no pueden guardar los dos.

Pruebas: `Dos_ejecutores_avanzando_el_mismo_Saga_a_la_vez_no_duplican_ningun_efecto` y el camino feliz.

## Compensación real (1.0)

Si el paso 3 (Fleet) agota sus reintentos, el ejecutor compensa en orden inverso: devuelve el pago de `Liberado` a `EnDisputa` y pide a Identity la revocación de la autorización. El estado de los otros contextos cambia de verdad; no se registra un mensaje y ya. Si la compensación misma falla porque Identity no responde, el Saga queda en `RequiereIntervencion` en vez de declararse compensado sin serlo.

Pruebas:

- `Si_Fleet_no_se_recupera_el_Saga_compensa_y_revierte_el_estado_de_los_pasos_anteriores`
- `Si_Identity_deja_de_responder_al_compensar_el_Saga_pide_intervencion_en_vez_de_fallar_en_silencio`
- `Si_alguien_libera_el_pago_a_mano_antes_que_el_Saga_el_Saga_compensa_y_no_libera_dos_veces`

## Idempotencia y Outbox (1.0)

**Outbox.** `Carga.Entregar` y el mensaje `EntregaConfirmada` se guardan en una sola transacción (`OutboxMensaje` en `EcoTrace.CargoTracking/Domain/`). El publicador (`OutboxDispatcherService`) es un `BackgroundService` que lee los pendientes y los entrega. Si Billing está caído, la entrega se registra igual y el evento sale cuando vuelve.

**Idempotencia por contexto.**

| Contexto | Operación | Cómo se logra | Prueba |
|---|---|---|---|
| Cargo & Tracking | Un `Entregado` repetido | No genera otro mensaje | `OutboxTests.Solo_la_entrega_genera_evento_y_lo_guarda_una_sola_vez` |
| Billing | El mismo `eventId` dos veces | Bandeja de entrada deduplica por `eventId` | `SagaLiberacionPagoTests.El_mismo_evento_entregado_dos_veces_crea_un_solo_Saga_y_no_mueve_el_dinero_dos_veces` |
| Billing | Un segundo evento para el mismo pago | Índice único: un Saga por `PagoId` | `SagaLiberacionPagoTests.Un_segundo_evento_para_el_mismo_pago_no_crea_otro_Saga_porque_la_clave_de_negocio_es_el_pago` |
| Identity | Autorizar dos veces el mismo pago | Devuelve la misma autorización, incluso en paralelo | `ContratosEntreServiciosTests.Autorizar_dos_veces_el_mismo_pago_es_idempotente_y_devuelve_la_misma_autorizacion` y `Autorizar_el_mismo_pago_a_la_vez_deja_una_sola_autorizacion` |
| Fleet | Reservar y liberar repetidos | `200` sin cambios | `ContratosEntreServiciosTests.Reservar_y_liberar_son_idempotentes_y_responden_si_cambiaron_algo` |
| Fleet | Dos reservas simultáneas | Solo gana una | `ContratosEntreServiciosTests.Dos_reservas_simultaneas_del_mismo_vehiculo_para_cargas_distintas_no_pueden_ganar_las_dos` |

La deduplicación de Billing usa las dos claves a la vez a propósito. El `eventId` frena el reenvío del mismo mensaje; la clave de negocio (el pago) frena un segundo mensaje distinto sobre el mismo pago. Con solo una, el dinero se puede mover dos veces.

## Resiliencia (0.5)

| Llamada | Tiempo por intento | Reintentos | Qué pasa al agotarlos |
|---|---|---|---|
| Billing a Identity (autorizar) | 500 ms | 2, esperando 100 ms y 200 ms | Falla cerrado: no se mueve dinero. |
| Billing a Fleet | 3 s | 3, esperando 2 s, 4 s y 8 s | Compensa. |
| Outbox a Billing | 3 s | espera exponencial (2 s, 4 s, 8 s, 16 s…), hasta 6 intentos | El mensaje pasa a `Muerto`; se reprocesa con `POST /api/outbox/{eventoId}/reprocesar`. |
| Billing a Identity (revocar) | 500 ms, 2 reintentos rápidos | hasta 4 intentos del paso, esperando 2 s, 4 s y 8 s | `RequiereIntervencion`. |

Un `4xx` no se reintenta, porque repetirlo no lo arregla; un `5xx` o un tiempo agotado sí.

Pruebas:

- `SagaLiberacionPagoTests.Si_Identity_no_responde_el_Saga_falla_cerrado_despues_de_los_reintentos_del_ADR_0002`
- `SagaConIdentityLentoTests.Un_Identity_que_tarda_mas_que_el_tiempo_maximo_agota_los_reintentos_y_el_Saga_falla_cerrado`
- `SagaLiberacionPagoTests.Si_Fleet_falla_dos_veces_el_Saga_se_recupera_con_reintentos_y_completa`
- `OutboxTests.Tras_agotar_los_intentos_el_mensaje_pasa_a_muertos_y_se_puede_reprocesar`
- `OutboxTests.Un_rechazo_permanente_deja_el_mensaje_muerto_de_inmediato_sin_gastar_reintentos`
- `OutboxTests.Solo_un_mensaje_muerto_se_puede_reprocesar`
- `SagaLiberacionPagoTests.Una_organizacion_suspendida_hace_fallar_el_Saga_en_el_primer_paso_sin_mover_dinero`

El modo de fallo simulado de Fleet (`SimulacionController`, `SimulacionFallos`) solo existe con `Simulacion__Habilitada=true`; sin esa variable las rutas responden `404`. Pruebas: `La_simulacion_de_fallos_esta_apagada_por_defecto` y `Con_la_simulacion_habilitada_se_puede_armar_y_restablecer_desde_la_API`.

## PR y revisión cruzada (0.5)

Este criterio es del proceso y no tiene código. Lo que sí se puede comparar es lo que cada escuadrón dejó en su PR: [#33](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/pull/33) (Identity), [#34](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/pull/34) (Cargo & Tracking) y [#35](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/pull/35) (Billing & Escrow).

## Qué mirar al comparar con su entrega

1. **Outbox y cambio de estado en la misma transacción**, o en dos escrituras separadas que pueden quedar a medias.
2. **Deduplicación por `eventId` y por clave de negocio**, o solo por una.
3. **Compensación que cambia el estado de los otros contextos en orden inverso**, o que solo escribe en el propio.
4. **`4xx` permanente distinguido de `5xx` transitorio** en los reintentos.
5. **Qué pasa si falla la compensación.** Un Saga que se da por compensado sin serlo es peor que uno que pide ayuda.

## Qué no cubre esta referencia

- Autenticación: las rutas no piden token, ni siquiera las llamadas entre servicios. Es el Trabajo 3.
- Un broker real. El transporte es HTTP, con el contrato del mensaje del ADR 0002.
- Una corrida larga con carga. Las pruebas prueban la corrección, no el rendimiento.
- Que el CI de este PR corra en verde en la nube se confirma en el PR, no en este documento.
