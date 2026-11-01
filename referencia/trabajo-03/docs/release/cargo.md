# Checklist de release: Cargo & Tracking

Resultados del 17 de octubre de 2026, con el build de Release contra la producción simulada. Lo común (capas 1 y 2, firma, qué no se verificó) está en [README.md](README.md).

**Acción encolada del módulo:** `Confirmar entrega` (`POST /api/cargas/{id}/seguimientos` con estado `Entregado`). Es una acción **crítica**: dispara el Saga que libera el pago, y no se puede encolar con la sesión degradada.
**Conflicto del módulo:** `B`, la carga fue reasignada a otro conductor o ya la entregó otra persona.

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 1 | La lista de cargas llega con el token firmado por producción | Sí | Cumple: «Actualizado hoy 8:07 p. m.», dos cargas del conductor. |
| 2 | Modo avión: la lista carga desde SQLite sin quedar en blanco | Sí | Cumple, con el banner de «Sin conexión» y la hora de la última sincronización. |
| 3 | `Confirmar entrega` sin conexión queda `PendienteSync` | Sí | Cumple («En cola»). |
| 4 | La acción sobrevive a cerrar y abrir la app | Sí | Cumple: `force-stop`, reabrir sin conexión, «En cola 1». |
| 5 | Al reconectar sincroniza sola y pasa a `Sincronizado` | Sí | Cumple («Enviadas 1»). |
| 6 | Un solo `Entregado` en el servidor y el Saga libera el pago | Sí | Cumple: seguimientos `Asignado, EnTransito, Entregado`; el pago pasó a `Liberado` con una sola liberación en la auditoría. |
| 7 | **No duplicación:** reenviar con la misma clave no repite el efecto | Sí | Cumple, con la salvedad del README (el Release no es depurable): se reenvió con la `Idempotency-Key` que la app usó (`21994ff3-325c-47c0-bd1c-81777332d6fa`) y respondió `200` con `Idempotent-Replayed: true`; siguió habiendo un solo `Entregado` y un solo pago liberado. Con el build de Debug, la propia app reenvió la operación (devuelta a `PendienteSync` en su base): Cargo respondió `200` «se devuelve el resultado anterior» y la operación volvió a `Sincronizado` con 2 intentos. |
| 8 | Con el token vencido y sin conexión, la acción crítica **no** se encola | Sí | Cumple (Debug): «Sin conexión y con la sesión vencida no se pueden confirmar entregas ni mover dinero. Se habilitará al reconectar.» |
| 9 | Un rechazo del servidor pasa a `Rechazado` con motivo y se conserva | Sí | Cumple (Debug): otra persona entregó la carga mientras no había señal; el servidor respondió `409` y la app mostró `Rechazada · Conflicto B · el servidor decidió; la acción se conserva`. |
| 10 | Conflicto B por **reasignación** de conductor en pantalla | No | **No probado en el dispositivo:** la referencia no tiene una ruta para reasignar una carga ya asignada. Lo cubren las pruebas del servidor. |
| 11 | Registros sin contraseñas ni tokens, con el esquema mínimo | Sí | Cumple en el servidor: cada solicitud deja `OperationName -> HttpStatus en DurationMs ms (entidad EntityId)` con su `CorrelationId`, y la prueba captura los registros reales y comprueba que ninguno lleva contraseñas, tokens (válido ni forjado) ni `Bearer `. (`RegistrosSegurosEnLosRecursosTests`). |

**Gate:** bloquean el release los pasos 1 a 9 y 11. Todos cumplen. El paso 10 no bloquea y queda sin probar en el dispositivo. La app móvil no escribe registros con este esquema: lo verificado es el servidor.

## Evidencia

- En la base de Cargo de producción, la clave guardada para la carga es la que se reenvió; el efecto no se repitió.
- `Idempotent-Replayed: true` y estado `200` en el reenvío, frente al `201` original.
