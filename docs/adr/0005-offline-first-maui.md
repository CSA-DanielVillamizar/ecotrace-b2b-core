# ADR 0005 — Diseño Offline-First de la App del Conductor (.NET MAUI)

**Estado:** Aceptado
**Clase:** PD174-3 · Clase 5 — Diseño UI Multiplataforma y Consumo Móvil (.NET MAUI)
**Cierra:** [Issue #18](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/18)

---

## Contexto

El conductor de EcoTrace no siempre está conectado: bodegas industriales, carreteras rurales y zonas de carga sin cobertura son parte normal de su jornada. Usamos un escenario para hacerlo tangible: el conductor confirma una entrega sin señal, la app falla en silencio sin guardar nada localmente, y tres días después Billing nunca libera el pago en Escrow — no porque el Saga (ADR 0003) o el JWT (ADR 0004) estuvieran mal diseñados, sino porque la acción que los dispara nunca salió del dispositivo.

Esta versión consolida el diseño real que entregaron los 4 equipos en la actividad de Verificación, con las correcciones de estandarización aplicadas tras la revisión cruzada.

### Regla de oro — Local primero, servidor después, nunca en silencio

Toda acción del conductor en cualquier pantalla de EcoTrace sigue el mismo ciclo, sin excepción:

1. **Guardar en SQLite local** con estado `PENDIENTE_SYNC`, antes de tocar la red.
2. **Actualizar la pantalla de inmediato** — el conductor nunca espera al servidor para ver el efecto de su acción.
3. **Sincronizar en segundo plano** cuando la Connectivity API detecte conexión, respetando el orden de las acciones encoladas.
4. **Nunca perder una acción en silencio** — si el servidor la rechaza, se lo decimos al conductor con una razón concreta, nunca con un reintento automático ciego ni con un descarte mudo.

Es el mismo principio del **Outbox Pattern** del ADR 0003, ahora del lado del cliente.

> **Estandarización aplicada (revisión cruzada, Clase 5) — `OperationId` obligatorio en los 4 módulos:** Identity propuso un `OperationId` único por acción encolada, citando explícitamente la clave de idempotencia ya establecida en el ADR 0002; Billing llegó de forma independiente a la misma idea con su propio `idempotency_key`. **Cargo & Tracking y Fleet Management no incluyeron ningún identificador único de operación en su diseño original.** Esto no es un detalle menor: sin un `OperationId`, un reintento de sincronización tras un corte de red a mitad de envío puede hacer que el servidor procese la misma acción dos veces — por ejemplo, disparando el Saga 2 "Liberar Pago en Escrow" (ADR 0003) por duplicado a partir de una única confirmación de entrega. Se adopta `OperationId` como **campo obligatorio en la tabla de cola local de los 4 módulos**, generado en el dispositivo en el momento en que la acción se guarda (paso 1 de la regla de oro), no al momento de sincronizar.

> **Estandarización aplicada (revisión cruzada, Clase 5) — un solo vocabulario de estados:** los 4 equipos usaron etiquetas distintas para el mismo ciclo de vida (`⏳Pendiente de sincronizar`, `Pendiente de sincronización`, `PENDIENTE_SYNC/SINCRONIZANDO/SINCRONIZADO`, `Pendiente/Enviado/Rechazado`). Se estandariza el enum único `PendienteSync → Sincronizando → Sincronizado | Rechazado` para la tabla de cola local de los 4 módulos — mismo dato, un solo nombre, para que un futuro dashboard de soporte pueda consultar cualquier módulo con la misma consulta.

---

## Diseño por módulo

### Identity — sesión y token

- **Qué se cachea:** los datos ya presentes en el JWT del último login (`UserId`, `Role`, `TenantId`) y la fecha de última sincronización, visible en pantalla para que el conductor sepa qué tan vigente es su sesión. La firma y expiración del token se siguen validando localmente, sin red — el JWT vive en `SecureStorage` (Keychain/Keystore), **nunca en SQLite ni en texto plano**.
- **Qué pasa si el token expira sin señal:** en vez de bloquear al conductor exigiendo conexión, la app ofrece una **sesión offline degradada** mediante autenticación biométrica local del dispositivo — el conductor prueba que sigue siendo el dueño físico del teléfono, con dos límites explícitos: (1) una ventana máxima desde el último contacto real con Identity (propuesta: 48h), pasada la cual se corta y se exige reconexión; (2) queda restringida a acciones que no muevan dinero ni cierren un ciclo de negocio crítico.
- **Acciones encoladas:** cambio de datos de perfil, cierre de sesión en otros dispositivos, registro de logout — cada una con su `OperationId`. El login inicial **no se encola nunca**: EcoTrace no guarda la contraseña del conductor para reintentarla después; sin sesión previa y sin red, no hay entrada.
- **Corrección de límite (revisión cruzada):** la biometría **no sustituye ni relaja** la validación de firma/expiración/issuer/audiencia del ADR 0004 — solo sostiene la continuidad de la interfaz mientras no hay red. Las acciones encoladas durante la sesión degradada se autorizan, al sincronizar, contra el **JWT nuevo** que Identity emite al reconectar — nunca contra el token vencido que habilitó la pantalla localmente. Esta distinción es la que evita que la propuesta de Identity se lea como una excepción a la regla de oro de Clase 4 ("ningún módulo arregla un token inválido").

### Fleet Management — vehículo, conductor y ruta

- **Qué se cachea:** ruta asignada, estado del vehículo, estado del conductor y puntos/paradas de la ruta, con fecha y hora de la última sincronización.
- **Qué se encola:** las acciones del conductor sobre su ruta y su vehículo (ej. iniciar/finalizar tramo, reportar estado del vehículo) se guardan en la tabla local de Sync Queue con estado `PendienteSync` y su propio `OperationId`.
- **Conflicto al sincronizar** — ejemplo concreto: el conductor inicia una ruta sin conexión; mientras está offline, un supervisor cancela o reasigna esa misma ruta desde el panel de despacho. Al reconectar, el servidor rechaza la acción porque la ruta ya no existe en el estado que el conductor asumía. La pantalla muestra el estado real del servidor (`Ruta cancelada` / `Ruta reasignada`) junto a la acción local que quedó sin efecto, y ofrece **Reintentar** (si la causa fue transitoria), **Descartar** (si el conductor confirma que ya no aplica) o **Enviar a revisión** (si el conductor considera que el cambio del despacho fue un error).
- **Corrección (revisión cruzada):** esta sección se eleva al mismo nivel de detalle que sus tres pares — el diseño original de Fleet Management era correcto en su principio ("el estado del servidor tiene prioridad, no se sobrescribe automáticamente") pero le faltaba el ejemplo concreto y el `OperationId`, ambos incorporados arriba.

### Cargo & Tracking — cargas y confirmación de entrega

- **Qué se cachea:** cargas asignadas, código/número de carga, origen y destino, estado (`Pendiente`/`Recogida`/`En tránsito`/`Entregada`), próxima parada, últimos eventos de tracking y fecha de última sincronización. Banner visible en toda pantalla sin conexión: *"⚠ Sin conexión — Mostrando datos guardados · Última sincronización: 10:30 a. m."*
- **Qué se encola:** cambios de estado de carga (Recogida, En tránsito, llegada/salida de parada, Entregada), reporte de incidencias (retraso, daño, cliente ausente, dirección incorrecta) y eventos de tracking que no lograron enviarse. Cada acción se guarda con su `OperationId`.
- **Conflicto al sincronizar** — ejemplo original del equipo, preservado tal cual por ser el más completo de los 4: el conductor marca *Carga #105 → Entregada* estando offline, pero la carga fue reasignada a otro conductor mientras tanto. Al reconectar, el servidor rechaza la entrega; la pantalla explica la causa (`Estado del servidor: Reasignada` vs. `Tu acción: Marcar como entregada`) y ofrece **Aceptar estado del servidor**, **Reintentar** o **Enviar a revisión**.
- Esta es la entrega que mejor equilibra los tres pasos de la regla de oro y sirve de referencia para las demás.

### Billing & Escrow — estado de pago

- **Qué se cachea:** entregas asignadas, estado (`Pendiente`/`En curso`/`Confirmada`), datos básicos de la entrega, estado del pago asociado (`Retenido`/`Pendiente`/`Pagado`), última actualización, e indicador visible de sin conexión.
- **Qué se encola:** confirmar entrega, registrar evidencia/foto, actualizar estado de entrega, capturar datos del recorrido — cada acción con su `idempotency_key` (equivalente al `OperationId` estandarizado arriba) y ciclo visible `Pendiente de sincronizar → Sincronizando → Sincronizado`.
- **Conflicto al sincronizar** — ejemplo original del equipo: el conductor confirma una entrega sin conexión; al reconectar, el servidor responde que *"la entrega ya fue cerrada por otro proceso"*. La acción no se descarta en silencio ni se marca como sincronizada — pasa de `PENDIENTE_SYNC` a `RECHAZADA`, se conserva el registro para auditoría, y la pantalla distingue explícitamente entre un **rechazo definitivo** (sin reintento automático) y un caso que **requiere revisión** de despacho.
- Billing es, junto con Identity, el equipo que más claramente separó "rechazo que no se negocia" de "conflicto que sí admite arbitraje" — insumo clave para la taxonomía unificada de abajo.

---

## Taxonomía unificada de conflictos de sincronización (síntesis de los 4 equipos)

Los 4 diseños, de forma independiente, distinguían tipos de rechazo distintos con reglas distintas — sin nombrarlos como tal. Se consolida una taxonomía única de 3 tipos, aplicable a los 4 módulos:

| Tipo | Qué significa | Quién gana | Ejemplo real de la actividad |
|---|---|---|---|
| **A — Validación recuperable** | El dato local no cumple una regla vigente, pero el conductor puede corregirlo. | El servidor informa el motivo; el conductor corrige y reintenta. | Identity: teléfono con formato inválido. |
| **B — Estado autoritativo no negociable** | Una fuente de autoridad (despacho, administración) cambió el estado mientras el conductor estaba offline. | El servidor gana siempre; no hay reintento automático, la acción local se descarta o marca como rechazada. | Identity: tenant suspendido o rol degradado. Billing: entrega cerrada por otro proceso. Cargo: carga reasignada a otro conductor. |
| **C — Conflicto de edición concurrente** | Dos actores legítimos modificaron el mismo recurso por caminos distintos; ninguno es automáticamente el correcto. | Se muestran ambas versiones o se envía a revisión humana — nunca "el último que sincroniza gana". | Identity: mismo campo de perfil editado desde el celular y desde la web. Fleet/Cargo: opción "Enviar a revisión". |

Esta tabla reemplaza el manejo ad hoc que cada equipo diseñó por separado y queda como el estándar de conflicto para cualquier módulo nuevo que EcoTrace agregue a futuro.

---

## Consecuencias

- Los 4 módulos comparten un único vocabulario de estados de sincronización (`PendienteSync/Sincronizando/Sincronizado/Rechazado`) y un único campo obligatorio de idempotencia (`OperationId`), eliminando el riesgo de doble procesamiento al reintentar una sincronización interrumpida.
- La taxonomía de 3 tipos de conflicto (A/B/C) se adopta como estándar transversal, reemplazando el diseño independiente de cada equipo.
- La sesión offline degradada de Identity queda documentada con su límite explícito: nunca sustituye la validación de JWT del ADR 0004, solo sostiene la UI mientras no hay red.
- Fleet Management queda nivelado con sus 3 pares en profundidad de diseño (ejemplo concreto, `OperationId`, conflicto tipado).
- El banner "Sin conexión — mostrando datos guardados + última sincronización" se adopta como componente de UI compartido entre los 4 módulos (mismo principio de reutilización de MVVM/View que vimos en la Diapositiva 8).

**No quedan pendientes abiertos de esta clase.** Las dos correcciones de consolidación (`OperationId` obligatorio y vocabulario único de estados) y la síntesis de la taxonomía de conflictos quedaron resueltas y documentadas arriba, con la justificación de por qué se hizo cada cambio.
