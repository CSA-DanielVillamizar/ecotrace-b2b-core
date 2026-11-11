# Checklist de release: Billing & Escrow

Resultados de verificación para el pase a Release contra la producción simulada (ADR 0006).

**Acción encolada del módulo:** `Registrar evidencia` (`POST /api/pagos/{id}/evidencias`). La confirmación de la entrega se realiza en Cargo y Billing la recibe por evento Outbox; por diseño, la acción encolada de este módulo no mueve dinero.  
**Conflicto del módulo:** `B`, el pago ya fue liberado por otro proceso mientras el dispositivo no tenía señal.

---

## Verificación del Módulo de Billing

| # | Paso de Verificación | Bloquea | Resultado | Detalle |
|---|---|---|---|---|
| 1 | La lista de pagos llega con el token de producción | Sí | Cumple | El middleware valida el `iss` y `aud` de producción simulada sin error. |
| 2 | Modo avión: la pantalla carga desde la cola local sin quedar en blanco | Sí | Cumple | Muestra la interfaz con los datos guardados y el banner de «Sin conexión». |
| 3 | La evidencia sin conexión queda `PendienteSync` | Sí | Cumple | La acción se guarda en la base SQLite local con su `OperationId`. |
| 4 | Al reconectar, sincroniza y el servidor la registra una sola vez | Sí | Cumple | El motor de sincronización despacha la evidencia y se registra en la auditoría del pago. |
| 5 | Registrar evidencia **no** mueve dinero | Sí | Cumple | El pago permanece en estado `EnCustodia` hasta que el Saga de entrega concluya. |
| 6 | El pago pasa a `Liberado` por el Saga una sola vez | Sí | Cumple | Tras recibir `EntregaConfirmada`, el Saga libera el pago e instruye a Fleet una única vez. |
| 7 | **No duplicación de la evidencia (`Idempotency-Key`)** | Sí | Cumple | Reintentos con el mismo `OperationId` responden `200 OK` sin registrar duplicados en `OperacionesPago`. |
| 8 | Conflicto B (pago ya liberado) | No | Cumple | Si el pago ya fue liberado, el backend responde `409 Conflict` con `tipoConflicto: "B"`. |
| 9 | Sesión degradada: acciones de dinero bloqueadas | Sí | Cumple | La sesión offline degradada respeta las restricciones de seguridad del ADR 0005. |
| 10 | Registros seguros (sin datos sensibles ni tokens) | Sí | Cumple | Los logs registran `OperationName`, `HttpStatus` y `CorrelationId`; no se registran tokens JWT, credenciales ni montos en texto plano. |

---

## Gate de Release

- **Criterio de bloqueo:** Bloquean el release los pasos 1 a 7, 9 y 10.
- **Resultado:** **Todos los pasos bloqueantes cumplen**. Se garantiza la integridad financiera: no hay riesgo de mover dinero real por duplicaciones de red ni filtraciones de credenciales.

