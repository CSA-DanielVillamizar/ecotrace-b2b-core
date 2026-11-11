# Checklist de release: Fleet Management

Resultados del 17 de octubre de 2026, con el build de Release contra la producción simulada. Lo común (capas 1 y 2, firma, qué no se verificó) está en [README.md](README.md).

**Acción encolada del módulo:** `Reportar novedad` (`POST /api/vehiculos/{id}/novedades`, ruta nueva de esta fase).
**Conflicto del módulo:** `B`, el vehículo fue reservado para otra carga mientras no había señal.

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 1 | El vehículo y la carga que llevaba llegan con el token de producción | Sí | Cumple: placa del vehículo de la siembra de producción y «Actualizado hoy 8:17 p. m.». |
| 2 | Modo avión: la pantalla carga desde SQLite sin quedar en blanco | Sí | Cumple, con el banner de «Sin conexión». |
| 3 | La novedad sin conexión queda `PendienteSync` | Sí | Cumple, **después de desbloquear con el PIN**: pasada la vigencia del token, la app pidió primero «desbloquea con tu huella o PIN» y solo entonces respondió «Guardada. Se enviará cuando haya conexión.» |
| 4 | Al reconectar sincroniza y el servidor la guarda una sola vez | Sí | Cumple: una fila en `NovedadesVehiculo` con «Espejo lateral roto». |
| 5 | La novedad lleva a quien la reportó | Sí | Cumple (probado con Debug): la fila trae el `userId` del conductor. |
| 6 | **No duplicación** | Sí | Cubierta por la prueba del servidor (`NovedadesDeFleetTests`: repetir la clave devuelve `200` y el mismo `novedadId`). **No** se forzó el reintento desde el teléfono para este módulo. |
| 7 | Conflicto C (otra persona cambió lo mismo) se muestra y no se reintenta solo | No | Cumple (Debug) **con un Fleet simulado** que responde con la forma real del `ProblemDetails`: `Rechazada · Conflicto C · otra persona cambió lo mismo; pide revisión`, sin botón de corregir, un solo envío. El servidor real lo produce y está probado (`ConflictoCDeConcurrenciaTests`). |
| 8 | Conflicto B (vehículo reservado para otra carga) en pantalla | No | **No probado en el dispositivo.** Cubierto por la prueba del servidor. |
| 9 | Registros sin contraseñas ni tokens, con el esquema mínimo | Sí | Cumple en el servidor: cada solicitud deja `OperationName -> HttpStatus en DurationMs ms (entidad EntityId)` con su `CorrelationId`, y la prueba captura los registros reales y comprueba que ninguno lleva contraseñas, tokens (válido ni forjado) ni `Bearer `. (`RegistrosSegurosEnLosRecursosTests`). |

**Gate:** bloquean el release los pasos 1 a 6 y 9. Todos cumplen; el 6 con la salvedad dicha (el reintento desde el teléfono no se forzó para este módulo). La app móvil no escribe registros con este esquema: lo verificado es el servidor.
