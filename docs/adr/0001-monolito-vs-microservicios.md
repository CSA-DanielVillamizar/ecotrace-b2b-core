# ADR 0001: Monolito vs Microservicios — Bounded Contexts de EcoTrace B2B

* **Estado:** Aceptado
* **Responsable técnico:** SantiagoMartinez

## Contexto

EcoTrace B2B tiene 4 dominios con perfiles muy distintos: `Identity` (poca escritura, seguridad crítica), `Fleet Management` (estado de conductores/rutas), `Cargo & Tracking` (ráfagas de telemetría offline-first) y `Billing & Escrow` (transacciones de dinero con rollback compensatorio). Había que decidir: ¿monolito modular o microservicios por dominio?

## Decisión

Microservicios por Bounded Context, cada uno con su propia base de datos. Comunicación síncrona vía **gRPC** y asíncrona vía eventos (**RabbitMQ / Azure Service Bus**). Los flujos que cruzan varios contextos (ej. entrega verificada → liberar pago) se coordinan con el **Patrón Saga**, no con transacciones ACID distribuidas.

Se descarta el monolito porque compartir base de datos entre un dominio tan volátil (telemetría) y uno tan crítico (pagos) es el riesgo más alto de todos (ver más abajo).

## Bounded Contexts

**`Identity Microservice`**
- Entidades: `User`, `Role`/`Claim`, `Tenant`.
- Referencias externas: ninguna — es el dominio base, los demás lo referencian a él por `UserId`/`TenantId`.
- No comparte: su base de datos ni las credenciales/roles internos.

**`Fleet Management`**
- Entidades: `Vehicle`, `Driver`, `Route`.
- Referencias externas: `UserId` (Identity) para asociar el conductor a su cuenta.
- No comparte: telemetría cruda ni datos de pagos.

**`Cargo & Tracking`**
- Entidades: `Shipment`, `TelemetryEvent`, `DeliveryProof`.
- Referencias externas: `VehicleId`/`DriverId` (Fleet); publica el evento `DeliveryVerified` que consume Billing.
- No comparte: datos financieros ni credenciales de dispositivos.

**`Billing & Escrow`**
- Entidades: `Invoice`, `EscrowAccount`, `PaymentTransaction`.
- Referencias externas: `TenantId` (Identity); se suscribe a `DeliveryVerified` (Cargo & Tracking).
- No comparte: su base de datos transaccional ni la lógica interna de reversas del Saga.

## Consecuencias

**Positivas:** escalado independiente (ej. Cargo & Tracking en picos de telemetría), aislamiento de fallos entre dominios, despliegues independientes por equipo.

**Negativas:** más complejidad operativa (orquestar 4 servicios + mensajería), consistencia eventual en vez de transacciones atómicas, curva de aprendizaje mayor (gRPC, Saga).

**Riesgo de acoplamiento si fuera monolito:** con una sola base de datos, sería fácil que `Billing` consultara directamente las tablas de `Shipment`/`TelemetryEvent` en vez de depender del evento `DeliveryVerified`. Como telemetría es el dominio que más cambia de esquema, cualquier migración ahí rompería silenciosamente la lógica de facturación (dinero real, baja tolerancia a errores) y bloquearía el despliegue independiente de ambos módulos.
