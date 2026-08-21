# ADR 0003 — Patrón Saga: Transacciones Distribuidas de EcoTrace B2B

**Estado:** Aceptado
**Clase:** PD174-3 · Clase 3 — Patrón Saga y Transacciones Distribuidas
**Cierra:** [Issue #14](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/14)

---

## Contexto

Clase 2 decidió cómo se hablan los servicios de EcoTrace (síncrono/asíncrono, eventos, timeouts, idempotencia). En Clase 3 enfrentamos la operación de negocio más delicada del sistema: el ciclo de vida completo de un envío, desde que se solicita el transporte hasta que se libera el pago — un proceso que atraviesa Identity, Fleet Management, Cargo & Tracking y Billing, cada uno con su propia base de datos.

Usamos un escenario hipotético para hacer tangible el riesgo de no coordinar esto explícitamente: Cargo & Tracking confirma una entrega, Billing libera el pago, pero el evento que le tocaba a Fleet Management (liberar el vehículo y el conductor) se pierde porque el servicio estaba caído en ese instante. El pago sale correctamente, pero el vehículo queda marcado como "ocupado" en el sistema para siempre — nadie compensó ese paso.

Citamos a Pat Helland (*"Life Beyond Distributed Transactions: An Apostate's Opinion"*, 2007): las transacciones distribuidas clásicas (2PC) no escalan en sistemas grandes porque bloquean a todos los participantes hasta que todos confirman. La alternativa es el **Patrón Saga**: transacciones locales encadenadas, cada una con su propia compensación explícita.

## Decisión

EcoTrace B2B coordina sus procesos de negocio distribuidos con **dos Sagas independientes**, cada una con **Orquestación** (un coordinador central, dado que Billing maneja dinero real y necesitamos un estado visible del proceso completo). Se documentan por separado porque tienen disparadores y ciclos de vida distintos — mezclarlos en una sola cadena fue el error de encuadre que cometieron los 4 equipos en la actividad de Verificación, y que esta versión corrige.

**Regla de oro (heredada de Clase 1):** ningún paso de un Saga accede a la base de datos de otro módulo. La coordinación ocurre exclusivamente por comandos/eventos, referenciando entidades por ID.

---

## Saga 1 — Iniciar Transporte

**Disparador:** se crea una nueva solicitud de transporte para un cargo. **Termina cuando:** el cargo queda asignado, con seguimiento activo.

| Paso | Módulo | Transacción local | Compensación |
|---|---|---|---|
| 1 | **Cargo & Tracking** (entidad `Carga`) | Reserva capacidad de transporte para la orden: `Disponible → Reservada`. Registra `orden_id`, `transportista_id`, `vehiculo_id`, `evento_id`. | Si un paso posterior falla: `Reservada → Cancelada/Disponible`. No se elimina el registro — se conserva evidencia de que existió y se revirtió. |
| 2 | **Fleet Management** | Reserva el conductor y el vehículo seleccionados: `Disponible → Reservado` en ambos recursos. Si ambos se reservan correctamente, confirma el paso. Publica el evento `RecursosReservados` (comunicación **asíncrona**, consistente con Clase 2). | Si un paso posterior falla, libera la reserva: `Reservado → Disponible` en conductor y vehículo. No se eliminan los registros — se deja constancia de que la reserva fue cancelada, por auditoría. Publica `RecursosLiberados`. |
| 3 | **Cargo & Tracking** (entidad `Seguimiento`) | Activa la sesión de seguimiento de la orden: `No_Iniciado → Activo`. Registra `orden_id`, `vehiculo_id`, `tracking_id`, fecha de inicio, `evento_id`. | Si un paso posterior falla: `Activo → Cancelado`. Deja de procesar nuevas ubicaciones para esa orden y libera los recursos de seguimiento asociados. No borra el historial de ubicación ya registrado. |

> **Nota de consolidación (revisión cruzada, Clase 3):** los equipos de Fleet, Cargo y Tracking entregaron pasos correctos y consistentes entre sí (mismo patrón `Disponible/Reservado/Cancelado`), pero ninguno declaró explícitamente que este era un Saga distinto al de liberación de pago. Se nombra aquí como "Saga 1" para eliminar esa ambigüedad. Cargo y Tracking son dos entidades del **mismo** Bounded Context (Cargo & Tracking, definido en el ADR 0001) actuando como dos pasos del mismo Saga — no son módulos nuevos ni en conflicto.

**Flujo asíncrono (coreografía dentro de este Saga, coordinada por el Orchestrator):**
```
Solicitud de transporte
  → Cargo & Tracking reserva capacidad (Disponible → Reservada)
  → Fleet Management verifica disponibilidad y reserva conductor + vehículo
  → Fleet publica RecursosReservados
  → Cargo & Tracking activa seguimiento (No_Iniciado → Activo)

Si falla un paso posterior:
  → Evento de compensación
  → Fleet libera conductor + vehículo (Reservado → Disponible) → publica RecursosLiberados
  → Cargo & Tracking cancela reserva y/o seguimiento (→ Cancelada / Cancelado)
```

---

## Saga 2 — Liberar Pago en Escrow

**Disparador:** Cargo & Tracking confirma que la entrega fue completada. **Termina cuando:** el pago queda liberado al transportista o, si algo falla, queda formalmente en disputa.

| Paso | Módulo | Transacción local | Compensación |
|---|---|---|---|
| 1 | **Identity** | Antes de que Billing libere el pago, Identity valida que el `Tenant` receptor esté activo y crea `PaymentAuthorization` (`TenantId`, `PaymentId`, `Estado = Autorizado`, fecha) en su propia base de datos. | Si un paso posterior falla, Identity **no borra** el registro — lo marca `Estado = Revocado`. Esa autorización ya no puede reusarse para otro intento con el mismo `PaymentId`. **Disparador de la compensación (resuelto en esta revisión):** el Orchestrator del Saga 2 envía el comando `RevocarAutorizacion(PaymentId)` a Identity — la entrega original no lo especificaba; sin este comando explícito, la compensación de Identity quedaba sin disparador. |
| 2 | **Billing** | Verifica que `PaymentAuthorization.Estado = Autorizado` (cierra el enlace con el paso 1, ausente en la entrega original) y cambia el estado del pago de `Retenido (Escrow) → Liberado` en `transacciones_pago`, registrando fecha, monto, `entrega_id` y `evento_id`. | Si un paso posterior falla, Billing revierte: `Liberado → Revertido a Escrow / En Disputa`, genera una nota de débito interna y emite un comando para congelar nuevamente los fondos. **Ajuste de esta revisión:** la entrega original condicionaba esto a la falla de "notificaciones" o "contabilidad general" — sistemas que ningún equipo diseñó en esta clase. Se reformula: la compensación de Billing se dispara si **Identity revoca la autorización** o si **Fleet Management no logra liberar el vehículo tras los reintentos** definidos en el Saga 1 (ver ADR 0002 para política de retry/backoff) — usando únicamente pasos ya definidos en este documento. |
| 3 | **Fleet Management** | Libera el vehículo y el conductor tras la entrega confirmada: `Reservado → Disponible` (mismo mecanismo del Saga 1, paso 2, ahora disparado por la confirmación de entrega en vez de por una reserva inicial). | Si el evento de liberación se pierde (ver Diapositiva 6, Clase 3), el Orchestrator reintenta con backoff; si sigue fallando tras N intentos, genera una alerta para intervención manual en vez de fallar en silencio. |
| 4 | **Auditoría Financiera** (Billing) | Registra la traza completa del Saga 2: autorización, liberación, y cualquier compensación ejecutada. | No aplica compensación — es un registro de solo lectura del historial; nunca se revierte, solo se completa el registro. |

---

## Consecuencias

- Ningún paso de ninguno de los dos Sagas bloquea a los demás mientras espera — cada transacción local confirma de inmediato, evitando el anti-patrón de 2PC.
- La consistencia entre ambos Sagas es eventual: puede haber una ventana breve donde, por ejemplo, un vehículo esté "Reservado" en Fleet mientras Cargo & Tracking aún no ha confirmado el seguimiento. Esto es aceptable y esperado.
- El **Outbox Pattern** (ADR de referencia: Diapositiva 10, Clase 3) debe aplicarse en los cuatro módulos que publican eventos (Cargo & Tracking, Fleet Management, Billing) para garantizar que ningún evento de estos Sagas se pierda al publicarse — independientemente de que el consumidor esté disponible o no en ese instante.
- Idempotencia obligatoria en todos los pasos y compensaciones: reintentar `ReservarConductor` o `RevocarAutorizacion` con la misma clave de operación nunca debe duplicar el efecto.

**No quedan pendientes abiertos de esta clase** — los tres huecos de integración detectados en la revisión cruzada (disparador de la revocación en Identity, dependencia de sistemas no diseñados en Billing, y verificación de la autorización de Identity antes de liberar en Billing) quedaron resueltos explícitamente arriba.
