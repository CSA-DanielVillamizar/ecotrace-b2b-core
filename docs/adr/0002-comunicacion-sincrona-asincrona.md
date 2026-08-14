# ADR 0002 — Comunicación síncrona y asíncrona entre Bounded Contexts

**Estado:** Aceptado  
**Clase:** PD174-3 · Clase 2 — Patrones de Comunicación Síncrona y Asíncrona  
**Cierra:** [Issue #12](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/12)  
**Fecha:** 2026-08-13

---

## Historia de Usuario (SDD)

**Como** equipo técnico de EcoTrace B2B,  
**Quiero** definir cuándo usar comunicación síncrona, asíncrona o híbrida entre los Bounded Contexts,  
**Para** reducir el acoplamiento temporal y mantener operaciones confiables cuando un servicio esté lento o no disponible.

---

## Contexto

La Clase 1 estableció cuatro Bounded Contexts independientes: **Identity**, **Fleet Management**, **Cargo & Tracking** y **Billing**. Cada módulo posee sus datos y ningún servicio puede acceder directamente a la base de datos de otro.

La separación de responsabilidades no elimina la necesidad de comunicarse. Una operación de EcoTrace puede requerir una decisión inmediata, como comprobar si un tenant está habilitado antes de mover dinero, o puede producir consecuencias que deben procesarse después, como iniciar el seguimiento de un cargo o registrar una auditoría.

Usar comunicación síncrona para todo produciría acoplamiento temporal: los servicios tendrían que estar disponibles simultáneamente, la latencia se acumularía y una falla podría propagarse por toda la cadena. Usar comunicación asíncrona para todo también sería incorrecto: algunas decisiones de seguridad y autorización requieren información actual antes de continuar.

Las propuestas de los equipos durante la actividad de Clase 2 analizaron tres interacciones reales:

1. **Identity ↔ Billing & Escrow:** verificar el estado actual del tenant antes de abrir o liberar un escrow.
2. **Cargo ↔ Tracking:** iniciar el seguimiento después de asignar un cargo.
3. **Cargo & Tracking → Billing:** notificar una entrega válida para iniciar la liberación de un pago.

La decisión debe cubrir disponibilidad, consistencia, timeout, reintentos, eventos duplicados y fallas permanentes.

---

## Decisión

EcoTrace utilizará un enfoque **híbrido**:

- **Comunicación síncrona** para decisiones críticas que deben resolverse con el estado actual antes de permitir una operación.
- **Comunicación asíncrona** para notificaciones, proyecciones, auditoría y consecuencias que pueden procesarse después.
- **Combinación síncrona/asíncrona** cuando una operación necesite una validación inmediata y luego un procesamiento distribuido.

Toda comunicación entre servicios debe usar contratos explícitos y referencias por identificadores. Ningún servicio accede directamente a la base de datos de otro.

### 1. Identity ↔ Billing & Escrow: validación síncrona del tenant

Antes de abrir un escrow o liberar un pago, Billing debe conocer el estado autoritativo y actual del tenant generador o transportista.

```text
Billing -> Identity.ValidateTenantStatus(tenantId) -> respuesta actual
Billing -> libera o bloquea la operación según la respuesta
```

La llamada será síncrona porque una proyección local puede tener algunos segundos de retraso y no es suficiente para autorizar un movimiento de dinero.

#### Contrato de resiliencia

- **Protocolo:** gRPC request/response.
- **Timeout:** 500 ms.
- **Reintentos:** máximo 2 reintentos, únicamente ante errores transitorios de red, timeout o indisponibilidad.
- **Backoff:** exponencial acotado, por ejemplo 100 ms y 200 ms, sin superar el tiempo máximo de la operación.
- **No reintentar:** respuestas válidas como `Active`, `Suspended` o `NotFound`.
- **Fallo cerrado:** si Identity no responde después de los reintentos, Billing bloquea la apertura o liberación del escrow y registra la causa.
- **Observabilidad:** registrar `CorrelationId`, `TenantId`, latencia, código de respuesta y resultado de la decisión.

Billing puede mantener una proyección local del estado para consultas no críticas, alimentada por eventos de Identity. Esa proyección no reemplaza la validación síncrona autoritativa cuando se va a mover dinero.

### 2. Identity → Billing: eventos de cambio de estado

Identity publicará eventos cuando cambie el estado operativo de un tenant:

- `TenantSuspended`
- `TenantReactivated`

Billing consumirá esos eventos para actualizar una bandera o proyección local y bloquear rápidamente operaciones futuras. Otros consumidores, como auditoría o notificaciones, podrán suscribirse sin que Identity conozca sus implementaciones.

#### Payload mínimo

```json
{
  "eventId": "uuid",
  "eventType": "TenantSuspended",
  "tenantId": "uuid",
  "tenantStatus": "Suspended",
  "occurredAt": "timestamp",
  "version": 7,
  "correlationId": "uuid"
}
```

`version` permite ignorar eventos atrasados y aplicar los cambios en orden lógico. `eventId` permite detectar duplicados.

### 3. Cargo → Tracking: evento `CargoAsignado`

Cargo & Tracking utilizará comunicación asíncrona para iniciar el seguimiento de un cargo después de registrar su asignación.

```text
Cargo registra la asignación
Cargo publica CargoAsignado
Tracking consume CargoAsignado cuando esté disponible
Tracking inicia el seguimiento
```

Cargo no debe quedar bloqueado esperando que Tracking esté disponible. La confirmación inmediata requerida por Cargo es la confirmación de que el mensaje fue aceptado y persistido por el broker, no la confirmación de que Tracking ya terminó de procesarlo.

#### Payload mínimo

```json
{
  "eventId": "uuid",
  "eventType": "CargoAsignado",
  "occurredAt": "timestamp",
  "cargoId": "uuid",
  "vehiculoId": "uuid",
  "conductorId": "uuid",
  "origen": "string",
  "destino": "string",
  "correlationId": "uuid"
}
```

#### Entrega confiable

- Cargo debe persistir la asignación y el evento mediante el patrón **Transactional Outbox**.
- Un publicador de Outbox entrega el evento al broker cuando la transacción local se confirma.
- La publicación al broker debe tener confirmación; si no se confirma, se reintenta.
- Tracking procesa el evento de forma idempotente usando `eventId` y `cargoId`.
- Los reintentos del consumidor usan backoff y un límite definido.
- Después de agotar los reintentos, el mensaje pasa a una **Dead Letter Queue (DLQ)** y se genera una alerta operativa.

No existe timeout de request/response entre Cargo y Tracking, pero sí debe existir un límite operativo para confirmar la publicación al broker y monitorear la antigüedad de mensajes pendientes.

### 4. Cargo & Tracking → Billing: evento `EntregaConfirmada`

Una entrega válida inicia el proceso financiero, pero el transportista no debe quedar bloqueado esperando a Billing o a la entidad financiera.

```text
Cargo & Tracking publica EntregaConfirmada
Billing consume y valida el evento
Billing solicita la liberación al proveedor financiero
Billing publica el resultado del procesamiento
```

La entidad financiera no consume directamente el evento de dominio de EcoTrace. Billing es el dueño del proceso de pago y coordina la integración financiera.

#### Payload mínimo

```json
{
  "eventId": "uuid",
  "eventType": "EntregaConfirmada",
  "occurredAt": "timestamp",
  "cargoId": "uuid",
  "paymentId": "uuid",
  "generadorTenantId": "uuid",
  "transportistaTenantId": "uuid",
  "amount": 0,
  "currency": "COP",
  "deliveryCertificateId": "uuid",
  "correlationId": "uuid"
}
```

#### Semántica de la respuesta

Una respuesta `200 OK` o confirmación equivalente del broker significa que el evento fue aceptado y persistido para procesamiento. No significa necesariamente que el dinero ya fue transferido.

Billing debe publicar o registrar por separado el resultado financiero, por ejemplo:

- `PaymentReleaseAccepted`
- `PaymentReleased`
- `PaymentReleaseRejected`

#### Reintentos y fallas permanentes

- Timeout de integración financiera: máximo 3 segundos por intento.
- Reintentos ante errores transitorios: 2 s, 4 s y 8 s, con máximo de 3 reintentos.
- No reintentar errores permanentes, como contrato inexistente, monto inválido o tenant suspendido.
- Después de agotar los reintentos, enviar el mensaje a la DLQ y generar una alerta para revisión.
- Billing debe mantener el pago en estado `En Custodia` hasta confirmar la liberación.

#### Idempotencia financiera

El evento lleva `eventId`, pero la operación financiera también debe tener una clave de negocio estable, `OperationId` o `PaymentId`.

Billing debe aplicar una restricción única sobre esa clave y guardar el resultado de la operación. Si recibe nuevamente el mismo evento o se repite una solicitud al proveedor financiero, debe devolver el resultado previamente registrado sin crear un segundo desembolso.

```text
idempotencyKey = PaymentId + ":release"
```

La liberación de un pago debe ser idempotente tanto en Billing como en el adaptador del proveedor financiero, cuando este soporte claves de idempotencia.

---

## Matriz consolidada de decisiones

| Interacción | Patrón | Motivo principal | Protección |
| --- | --- | --- | --- |
| Billing → Identity: validar tenant antes de mover dinero | Síncrono | Requiere estado actual y decisión inmediata | 500 ms, 2 retries transitorios, fail-closed |
| Identity → Billing: tenant suspendido/reactivado | Asíncrono | Actualizar proyecciones y reaccionar a cambios | `EventId`, `version`, consumidor idempotente, DLQ |
| Cargo → Tracking: `CargoAsignado` | Asíncrono | Tracking puede procesar después | Outbox, confirmación del broker, retries, DLQ |
| Cargo & Tracking → Billing: `EntregaConfirmada` | Asíncrono | La entrega no depende de Billing disponible | `EventId`, `PaymentId`, retries, DLQ, idempotencia |
| Billing → proveedor financiero: liberar escrow | Síncrono o asíncrono según contrato del proveedor | Requiere integrar el resultado financiero | timeout, backoff, clave de idempotencia, conciliación |

---

## Consecuencias

### Positivas

- Identity no queda acoplado temporalmente a Billing, Tracking o a otros consumidores de sus eventos.
- Billing no libera fondos basándose únicamente en un estado local potencialmente atrasado.
- Cargo puede registrar asignaciones y entregas aunque Tracking o Billing estén temporalmente indisponibles.
- Los eventos pueden tener varios consumidores sin modificar al productor.
- Los retries controlados, DLQ e idempotencia reducen duplicados y pérdida silenciosa de mensajes.
- La confirmación de aceptación del evento queda separada del resultado final del proceso financiero.

### Costos y riesgos

- La comunicación asíncrona introduce consistencia eventual y requiere monitorear mensajes pendientes.
- Outbox agrega almacenamiento y un proceso publicador por servicio.
- DLQ requiere alertas, herramientas de inspección y procedimientos de reprocesamiento.
- La idempotencia debe diseñarse en cada consumidor y no puede asumirse automáticamente por usar un broker.
- La validación síncrona de Identity puede bloquear operaciones legítimamente durante una caída del servicio, pero este costo es intencional para proteger los fondos.

---

## Criterios de Aceptación

- [x] El documento sigue el formato ADR: Contexto, Decisión y Consecuencias.
- [x] Se documenta una interacción síncrona: `Billing → Identity.ValidateTenantStatus`.
- [x] Se documentan interacciones asíncronas: `CargoAsignado`, `TenantSuspended`, `TenantReactivated` y `EntregaConfirmada`.
- [x] Cada llamada síncrona define timeout, manejo de errores, retries y backoff.
- [x] Cada evento define productor, consumidores, payload mínimo, reintentos y DLQ.
- [x] Las operaciones repetibles tienen `EventId`, `PaymentId` u `OperationId` y consumidores idempotentes.

---

## Relación con la siguiente clase

Esta decisión define los canales y contratos de comunicación. La Clase 3 utilizará estos flujos para estudiar el **Patrón Saga**, incluyendo orquestación, coreografía, compensaciones y recuperación de una transacción distribuida cuando falla uno de sus pasos.
