# Checklist de release: Billing & Escrow

Resultados del 17 de octubre de 2026, con el build de Release contra la producción simulada. Lo común (capas 1 y 2, firma, qué no se verificó) está en [README.md](README.md).

**Acción encolada del módulo:** `Registrar evidencia` (`POST /api/pagos/{id}/evidencias`). La confirmación de la entrega se hace en Cargo y Billing la recibe por evento; por eso la acción de este módulo no mueve dinero.
**Conflicto del módulo:** `B`, el pago ya salió de la custodia mientras no había señal.

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 1 | La lista de pagos llega con el token de producción | Sí | Cumple: `$ 310,000 COP · En custodia` y el pago ya liberado de la entrega. |
| 2 | Modo avión: la pantalla carga desde SQLite sin quedar en blanco | Sí | Cumple, con el banner de «Sin conexión». |
| 3 | La evidencia sin conexión queda `PendienteSync` | Sí | Cumple («Registrar evidencia · Billing · En cola»). |
| 4 | Al reconectar sincroniza y el servidor la registra una sola vez | Sí | Cumple: la auditoría del pago agrega «Evidencia de entrega registrada» una vez. |
| 5 | Registrar evidencia **no** mueve dinero | Sí | Cumple: el pago sigue `EnCustodia`. |
| 6 | El pago de una entrega confirmada pasa a `Liberado` por el Saga, una sola vez | Sí | Cumple (con la entrega de Cargo): una sola liberación en la auditoría, tras el envío y tras el reenvío con la misma clave. |
| 7 | **No duplicación de la evidencia** | Sí | Cubierta por la prueba del servidor (`EvidenciasDeBillingTests`). **No** se forzó el reintento desde el teléfono para este módulo. |
| 8 | Conflicto B (pago ya liberado) en pantalla | No | **No probado en el dispositivo.** Cubierto por la prueba del servidor («ya fue liberado por otro proceso»). |
| 9 | Con la sesión degradada, mover dinero está bloqueado | Sí | Cumple por la política de sesión (`PoliticaDeSesion`): la acción crítica no se encola. Probado con Cargo, que es la acción crítica de la cadena. |
| 10 | Registros sin números de tarjeta ni datos de contacto completos, con el esquema mínimo | Sí | Cumple en el servidor: cada solicitud deja `OperationName -> HttpStatus en DurationMs ms (entidad EntityId)` con su `CorrelationId`, y la prueba captura los registros reales y comprueba que ninguno lleva contraseñas, tokens (válido ni forjado) ni `Bearer `. (`RegistrosSegurosEnLosRecursosTests`). No se registran cuerpos de solicitud, así que tampoco montos ni referencias. |

**Gate:** bloquean el release los pasos 1 a 7, 9 y 10. Todos cumplen; el 7 con la salvedad dicha (el reintento desde el teléfono no se forzó para este módulo). La app móvil no escribe registros con este esquema: lo verificado es el servidor.
