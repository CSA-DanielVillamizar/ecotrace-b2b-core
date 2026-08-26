# ADR 0004 — OAuth2 y JWT: Autenticación y Autorización de EcoTrace B2B

**Estado:** Aceptado
**Clase:** PD174-3 · Clase 4 — Seguridad en Redes Distribuidas
**Cierra:** [Issue #15](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/15)

---

## Contexto

Desde el ADR 0001 (Clase 1), Identity posee las entidades `User`, `Role/Claim` y `Tenant`, y se estableció que el `TenantId` viaja en el JWT junto al `UserId` y el `Role`. Hasta ahora no habíamos definido formalmente cómo se emite ese token, cómo lo valida cada módulo, ni qué pasa si cae en manos equivocadas.

Usamos un escenario para hacerlo tangible: el JWT de un conductor se filtra en el wifi público de una estación de servicio. El token sigue siendo válido por horas. Alguien más lo usa para marcar una entrega como confirmada; Cargo & Tracking publica el evento; Billing, siguiendo exactamente el Saga definido en el ADR 0003, libera el pago. El Saga funcionó perfecto — el problema no fue la lógica de negocio, fue no proteger correctamente **quién** tenía permiso de decir "confirmo la entrega".

También citamos la clase de vulnerabilidad **"alg:none"**: librerías JWT que confiaban ciegamente en el algoritmo declarado dentro del propio token, permitiendo forjar tokens sin firma válida. La lección: decodificar un JWT no es lo mismo que validarlo.

## Decisión

EcoTrace B2B usa **OAuth2** como marco de autorización delegada: **Identity es el único Authorization Server** (emite tokens, es el único que conoce contraseñas); cada módulo (Fleet Management, Cargo & Tracking, Billing) es un **Resource Server** que valida tokens **localmente**, sin llamar a Identity en cada request — el mismo principio de bajo acoplamiento del ADR 0001, aplicado a identidad.

Esta versión consolida el diseño real de validación que entregaron los 4 equipos en la actividad de Verificación, con una corrección de estandarización aplicada tras la revisión cruzada.

### Regla de oro — validación obligatoria en todo módulo, sin excepción

Antes de confiar en **cualquier** claim del JWT, todo módulo de EcoTrace verifica, en este orden:

1. **Firma** — con la clave pública de Identity (JWKS).
2. **Expiración (`exp`)** — rechazar si ya venció, sin importar que la firma sea válida.
3. **Issuer (`iss`)** — confirmar que el token fue emitido por el Identity de EcoTrace, no por otro entorno (ej. staging).
4. **Audiencia (`aud`)** *(estandarizado en esta revisión — ver más abajo)* — confirmar que el token fue emitido para este módulo específico, no para otro.

Si cualquiera de las cuatro falla: **rechazo limpio** (401 Unauthorized). Ningún módulo intenta "arreglar" un token — no reinterpreta claims faltantes, no asume un rol por defecto, no lo refresca automáticamente en ese momento.

> **Estandarización aplicada (revisión cruzada, Clase 4):** Billing y Cargo & Tracking agregaron por su cuenta la verificación de `aud`, sin que se les pidiera. Es una protección real (evita que un token emitido para Fleet Management se reutilice contra Billing), así que se adopta como **obligatoria para los 4 módulos** — incluyendo Fleet Management e Identity, cuyas entregas originales no la mencionaban.

---

## Validación por módulo

### Identity (caso especial: también es Resource Server de sí mismo)

Identity emite los tokens, pero también expone endpoints administrativos propios (suspender un `Tenant`, cambiar un `Role`) que requieren autenticación igual que cualquier otro módulo.

- **Claims que lee:** `UserId` (quién pide la acción), `Role` (ej. solo un Admin puede suspender un Tenant), `TenantId` (para no permitir que alguien actúe sobre datos de otro tenant).
- **Verificación distintiva:** Identity es el **único módulo que valida la firma con su propia clave** (no depende de un JWKS externo, porque es quien la generó). Verifica `issuer` explícitamente para evitar que un token de un entorno distinto (ej. staging) se use en producción.
- **Token inválido:** rechazo con 401 limpio. Registra el intento en el log (posible indicio de token manipulado), **sin** devolver al cliente el motivo exacto del rechazo, para no darle información útil a quien esté probando un ataque.

### Fleet Management

- **Claims que lee:** `UserId`, `Role`, `TenantId`, `aud`.
- **Verificación:** firma, `exp`, `issuer`, `aud`.
- **Token inválido:** rechazo con 401/403 según el caso, sin intentar corregir el token.
- **Caso especial — telemetría de alta frecuencia (GPS cada 10 segundos):** para esta acción específica, Fleet Management **no** repite la autorización por `Role` en cada mensaje — el rol ya se validó al iniciar la sesión, y enviar una coordenada no es una acción privilegiada. **Esto no exime la validación de firma, expiración, issuer ni audiencia — esas cuatro se verifican siempre, en cada request, sin excepción.** Lo que se omite es únicamente la decisión de autorización basada en `Role`, no la autenticación del token.

### Cargo & Tracking

- **Claims que lee:** `UserId`/`sub`, `Role` (ej. solo Conductor o Despachador puede confirmar una entrega), `TenantId`, `aud`, y opcionalmente `iat` y `jti`.
- **Verificación:** firma, `exp`, `issuer`, `aud`.
- **`jti` (JWT ID):** no es obligatorio hoy, pero queda disponible como base para una futura lista de revocación si el sistema necesita rastrear o invalidar tokens específicos (ver sección de Revocación más abajo).
- **Token inválido:** rechazo total de la acción, sin intento de corrección.

### Billing

- **Claims que lee:** `UserId`, `Role`, `TenantId`, `exp`, `iss`.
- **Verificación:** firma, `exp`, `issuer`, `aud`, y autorización explícita por `Role` y `TenantId` antes de ejecutar cualquier operación financiera.
- **Token inválido:** distingue explícitamente entre **401 Unauthorized** (token inválido/expirado — no se procesa la solicitud, no se libera Escrow, se registra el intento en auditoría) y **403 Forbidden** (token válido, pero sin permisos suficientes para la operación).

> **Principio de negocio destacado (aporte de Billing, revisión cruzada Clase 4):** *"Que un JWT sea válido criptográficamente no significa necesariamente que la operación de negocio sea legítima."* Un token robado —como el del escenario de la Diapositiva 6— puede tener firma válida, `exp` vigente, `issuer` correcto y `aud` correcto, y aun así representar una suplantación. Por eso, para operaciones críticas como liberar un pago en Escrow, Billing no se apoya únicamente en la validación del JWT: aplica controles adicionales de autorización y trazabilidad (registro detallado de cada liberación, asociado a `UserId`, `TenantId` y `entrega_id`) que permiten auditar y disputar una operación después, aunque el token usado haya sido técnicamente válido. El diseño de controles anti-fraude más profundos (ej. geofencing, límites de velocidad de operación) queda fuera del alcance de esta clase y se deja como línea de trabajo futura, no como pendiente de este ADR.

---

## Consecuencias

- Ningún módulo, aparte de Identity, conoce ni maneja contraseñas.
- La validación de JWT es local y sin red en los cuatro módulos — consistente con el bajo acoplamiento del ADR 0001.
- La verificación de `aud` ahora es un estándar transversal, no una decisión aislada de dos equipos.
- Billing deja constancia explícita de que la seguridad de un Saga financiero no termina en la validación criptográfica del token — es una capa, no la única defensa.

### Revocación (Access Token vs. Refresh Token)

- El **Access Token** vive minutos u horas; si se roba, la ventana de exposición es corta por diseño.
- El **Refresh Token** vive más tiempo pero solo sirve para pedir un Access Token nuevo. Es la única pieza de este diseño que deja de ser completamente *stateless*: si se detecta que fue robado, Identity lo revoca contra una lista de tokens invalidados.
- El claim `jti` (propuesto por Cargo & Tracking) queda como mecanismo disponible para extender la revocación a Access Tokens específicos si en el futuro se necesita, sin que esto sea una implementación obligatoria hoy.

**No quedan pendientes abiertos de esta clase.** La única corrección de consolidación (estandarizar `aud` en los 4 módulos) quedó resuelta y documentada arriba, con la justificación de por qué se hizo el cambio.
