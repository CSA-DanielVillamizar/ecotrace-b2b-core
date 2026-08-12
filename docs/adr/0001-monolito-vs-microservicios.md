# ADR 0001 — Monolito vs. Microservicios y Bounded Contexts de EcoTrace B2B

**Estado:** Aceptado
**Clase:** PD174-3 · Clase 1 — De Monolitos a Microservicios
**Cierra:** [Issue #9](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/9)

---

## Contexto

EcoTrace B2B nació en Clase 0 dividido en 4 módulos de negocio (Identity, Fleet Management, Cargo & Tracking, Billing), cada uno con su propia Historia de Usuario convertida en Issue real. En Clase 1 nos preguntamos explícitamente: **¿por qué está dividido así, y no en un solo proyecto?**

Usamos un escenario hipotético para hacer tangible el riesgo de no dividir: un cambio pequeño en Fleet Management (agregar un campo al registro de conductores) bloquea, en un monolito, una tabla que Billing necesita para liberar pagos en Escrow — tumbando el sistema de pagos sin que nadie haya tocado Billing directamente. Este es el riesgo central que motiva la decisión.

Citamos a Martin Fowler (*"MonolithFirst"*): la mayoría de las migraciones exitosas a microservicios parten de un monolito bien diseñado y se dividen cuando el dolor de no dividir supera el dolor de dividir. Para EcoTrace, ese punto ya se cumple: 4 equipos distintos (roles asignados desde Clase 0) necesitan desplegar y evolucionar a velocidades distintas.

## Decisión

EcoTrace B2B se construye como **microservicios**, con **4 Bounded Contexts** que coinciden con los 4 módulos de negocio ya definidos en el Caso de Negocio (`docs/business-case.md`):

- **Identity**
- **Fleet Management**
- **Cargo & Tracking**
- **Billing**

**Regla de oro (aplicada en toda la arquitectura):** un módulo puede referenciar a otro únicamente por su identificador (ID). Ningún módulo accede directamente a la base de datos de otro. Se divide por **capacidad de negocio**, nunca por capa técnica — evitando el anti-patrón del "Monolito Distribuido".

Cada Bounded Context fue documentado y validado en la actividad de Verificación de Clase 1 (4 equipos, revisión cruzada). Las entradas originales de cada equipo fueron revisadas y corregidas antes de consolidarse aquí.

---

## Bounded Contexts

### Identity

**Entidades propias:** `User` (cuenta del usuario: conductor, supervisor, admin), `Role/Claim` (roles y permisos), `Tenant` (empresa/organización a la que pertenece el usuario).

**Referencias externas:** ninguna. Identity es el dominio base — los demás módulos lo referencian a él vía `UserId`/`TenantId`, nunca en sentido contrario.

**Qué NO comparte:** acceso directo a la tabla `User`/`Role` — los demás módulos solo reciben el `UserId` y el rol dentro de un token (JWT), nunca una consulta directa.

**Riesgo de acoplamiento (monolito):** si otros módulos tuvieran acceso directo a `User`/`Role`, cualquier cambio de esquema en Identity (ej. agregar verificación de identidad) rompería en cascada a todos los servicios. Además, un bug en otro módulo podría exponer directamente credenciales o roles, al no existir aislamiento del dominio más sensible en seguridad.

### Fleet Management

**Entidades propias:** `Conductor`, `Vehículo`.

> ⚠️ **Nota de resolución (revisión cruzada, Clase 1):** el equipo propuso originalmente una entidad `Asignación`, pero ese nombre ya pertenece a Cargo & Tracking (ver [Issue #5](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/5), creado en Clase 0). Se retira de aquí. Si Fleet necesita un concepto de "qué conductor maneja habitualmente qué vehículo" (roster fijo, distinto de la asignación puntual de un cargo), debe documentarse con un nombre distinto en una futura iteración.

**Referencias externas:** `UserId` de Identity (para identificar al coordinador/usuario que gestiona el registro).

**Qué NO comparte:** disponibilidad y datos completos de `Conductor`/`Vehículo` — los demás módulos solo reciben `ConductorId`/`VehículoId`.

**Riesgo de acoplamiento (monolito):** si otros módulos leyeran directamente la disponibilidad o los datos completos de conductores y vehículos, quedarían fuertemente dependientes de Fleet — un cambio interno en Fleet (como el del escenario de la Diapositiva 6) afectaría a los demás sin previo aviso.

### Cargo & Tracking

**Entidades propias:** `Carga`, `Asignación de Carga` (vehículo + conductor + cargo, ver Issue #5), `Seguimiento` (ubicación y estado del recorrido: Asignado, En tránsito, Con novedad, Entregado — ver Issues [#6](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/6) y [#7](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/7)).

**Referencias externas:** `VehículoId`/`ConductorId` de Fleet Management, `UserId` de Identity (cuando se necesitan datos del generador de carga o del auditor).

**Qué NO comparte:** acceso directo a los datos de conductores/vehículos de Fleet — solo IDs.

**Riesgo de acoplamiento (monolito):** acceder directamente a los datos o disponibilidad de vehículo/conductor en Fleet generaría acoplamiento fuerte e inconsistencias entre ambos módulos.

### Billing

**Entidades propias:** `Facturación` (documento/comprobante), `Pago` (monto cobrado, intento de pago, **estado de Escrow**: En Custodia / Liberado / Reembolsado — ver [Issue #8](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/8)), `Auditoría Financiera` (trazabilidad de las transacciones).

**Referencias externas:** `GeneradorId` y `TransportistaId` de Identity, `CargoId` de Cargo & Tracking (para saber cuándo liberar el pago al confirmarse la entrega).

> ⚠️ **Nota de corrección (revisión cruzada, Clase 1):** la entrega original de este equipo usó "clientes" y "Transportistas" como referencias, y "trazabilidad(operacion)" en vez de `CargoId`. Es la misma corrección de vocabulario ya aplicada en el Issue #8 en Clase 0 (EcoTrace es logística B2B, no e-commerce: no hay "clientes" ni "productos", hay generadores de carga y transportistas). Se corrige aquí para mantener consistencia con el resto del proyecto.

**Qué NO comparte:** acceso directo a `Facturación`/`Pago` desde otros módulos.

**Riesgo de acoplamiento (monolito):** si Fleet Management o Identity tuvieran acceso directo a Facturación, podrían generar bloqueos y causar caída del sistema de facturación y pagos — se perdería disponibilidad para procesar pagos y emitir facturas. Es el mismo patrón dramatizado en la Etapa 2 de esta clase (el lunes que Backend Core rompió Billing).

---

## Consecuencias

- Cada módulo se despliega de forma independiente; ningún cambio en un módulo puede bloquear directamente a otro a nivel de base de datos.
- Toda comunicación entre módulos ocurre por referencia de ID — el patrón de comunicación real (síncrono vía gRPC/REST vs. asíncrono vía mensajería) se define en la próxima clase.
- La consistencia de datos entre módulos ya no es automática (no hay una única transacción de base de datos que abarque todo) — se resolverá más adelante en el semestre con el **Patrón Saga**.

### Pendientes abiertos (a resolver en próximas clases)

1. **`Tenant` es una decisión transversal no cerrada.** Identity introdujo el concepto de `Tenant` (empresa/organización). Billing probablemente necesita `TenantId` en vez de conceptos ad-hoc como "clientes"/"Transportistas". Falta definir con los 4 equipos: ¿un `Tenant` es una empresa generadora de carga, una empresa transportista, o ambas? ¿Necesitan `Fleet` y `Cargo & Tracking` también un `TenantId` para aislar datos entre distintas empresas?
2. **Rol "supervisor" en Identity** podría ser la formalización del actor "coordinador logístico"/"gestor de flota" que quedó pendiente de definir en el Caso de Negocio desde Clase 0 — confirmar y actualizar `docs/business-case.md` si aplica.
3. **Nombre definitivo del concepto de "roster" en Fleet Management**, si el equipo determina que es distinto de `Asignación de Carga`.
