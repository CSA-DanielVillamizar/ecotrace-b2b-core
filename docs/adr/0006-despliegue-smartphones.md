# ADR 0006 — Despliegue de la App del Conductor en Smartphones

**Estado:** Aceptado
**Clase:** PD174-3 · Clase 6 — Despliegue en Smartphones (última clase del semestre)
**Cierra:** [Issue #20](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/20)

---

## Contexto

Un build que compila y funciona en el dispositivo de pruebas no está listo para producción. Usamos un escenario para hacerlo tangible: el Escuadrón Móvil está a un clic de publicar la primera versión, y una revisión de rutina descubre que el build de Release está firmado con el Keystore de Debug y apunta al endpoint de staging usado en Clase 5. Nada de eso falla ese día — el riesgo aparece después, cuando alguien puede firmar una actualización falsa o cuando producción lleva semanas hablándole a un servidor de pruebas sin que nadie lo note.

Esta versión consolida el checklist de release real que entregaron los 4 equipos en la actividad de Verificación, con las correcciones de estandarización aplicadas tras la revisión cruzada.

### Regla de oro — Dos capas de verificación, nunca una sola

Ningún checklist de release de EcoTrace se conforma con revisar la configuración. Se exige, para los 4 módulos:

1. **Capa 1 — Configuración:** el build está en configuración Release (no Debug), y el endpoint/URL base sale de `appsettings.Production` o su equivalente — nunca hardcodeado, nunca reutilizado de Staging.
2. **Capa 2 — Comportamiento real:** se confirma, con un dato conocido de producción, que el backend que responde es realmente el de producción — no basta con que la variable de entorno "diga" lo correcto.
3. **Gate explícito:** cada checklist declara qué pasos, si fallan, **bloquean el release**. Ningún checklist queda como una lista de buenas intenciones sin consecuencia.

> **Estandarización aplicada (revisión cruzada, Clase 6) — verificación en dos capas obligatoria:** Identity usa la validación de `issuer` del ADR 0004 como red de seguridad real — si el build apunta a staging por error, los tokens llevan el issuer de staging y producción los rechaza limpiamente, "no se cae bonito, se cae de una". Cargo & Tracking no se conforma con la URL configurada: abre un envío conocido de producción y confirma que los datos devueltos son reales, con una regla explícita — *"si por error el build apunta a Staging, el release no debe aprobarse aunque la pantalla funcione"*. Billing señala el riesgo asimétrico: una app mal configurada podría ejecutar **transacciones reales de dinero** desde lo que debería ser un ambiente de prueba — un fallo peor que el caso inverso. **Fleet Management, en su entrega original, se quedaba solo en la Capa 1** (revisar la variable de entorno inyectada al compilar). Se corrige añadiendo su propia Capa 2: verificar que un vehículo o ruta conocidos de producción devuelvan datos reales, no un fixture de staging — y se agrega el gate explícito que su entrega original no declaraba.

> **Estandarización aplicada (revisión cruzada, Clase 6) — prueba de no-duplicación obligatoria en la Prueba Mínima:** Billing es el único equipo que probó explícitamente forzar un reintento de sincronización y confirmar que **no se genera un pago duplicado** — la única validación real, en producción, de que el `OperationId` estandarizado en el ADR 0005 (Clase 5) efectivamente evita el doble procesamiento. Cargo & Tracking lo prueba de forma implícita (confirma que un evento nuevo "no se duplica"). **Identity y Fleet Management no incluían este paso**, a pesar de que sus propias acciones encoladas (cambio de perfil, acciones sobre una ruta) tienen el mismo riesgo si el reintento ocurre tras un corte de red a mitad de sincronización. Se adopta como paso obligatorio de la Prueba Mínima en los 4 módulos: reconectar, forzar un reintento, confirmar que el servidor no procesa la acción dos veces.

---

## Checklist por módulo

### Identity — sesión y token

- **Endpoint correcto:** authority/issuer y URL del JWKS salen de `appsettings.Production.json`, nunca de constantes en código. Se verifica que el build sea Release y que la pantalla "Acerca de" muestre el ambiente y la versión. Capa 2 (red de seguridad real): la validación de `issuer` del ADR 0004 — si el build apuntara a staging, producción rechaza esos tokens de forma limpia y visible, no silenciosa.
- **Nada sensible en logs:** nunca contraseña (ni enmascarada), nunca el JWT completo ni el refresh token, nunca la clave privada de firma ni secretos de configuración. Solo `UserId`, `TenantId` y un `correlation id`. Login fallido registra "credenciales inválidas" a secas, sin indicar cuál campo falló — igual que en la respuesta al cliente, evitando enumeración de usuarios.
- **Prueba mínima (gate explícito — si falla, no sale el release):** (1) login válido entra y ve solo las opciones de su rol; (2) contraseña incorrecta da error genérico; (3) modo avión con sesión iniciada no deja la pantalla en blanco, muestra caché y permite entrar por biometría (ADR 0005); (4) al reconectar, el token se revalida contra Identity sin pedir login de nuevo.
- **Corrección (revisión cruzada):** se agrega el paso de no-duplicación — reconectar y forzar el reintento de una acción encolada (ej. cambio de perfil) y confirmar que Identity no la procesa dos veces.

### Fleet Management — vehículo, conductor y ruta

- **Endpoint correcto:** se verifican las variables de entorno inyectadas al compilar en MAUI, confirmando que la URL base apunta estrictamente al dominio de producción.
- **Nada sensible en logs:** nunca JWT, credenciales, PINs de seguridad, ni trazas completas de coordenadas GPS en crudo que expongan la privacidad de rutas o conductores.
- **Prueba mínima (gate explícito — corrección de la revisión cruzada):** modo avión: las rutas cargan desde SQLite sin pantalla en blanco; las acciones se encolan con estado `PendienteSync`; al reconectar, sincronización y resolución de conflictos funcionan sin romper la app. **Si cualquiera de estos tres pasos falla, no sale el release.**
- **Corrección (revisión cruzada):** se agrega la Capa 2 que faltaba — confirmar, con un vehículo o ruta conocidos de producción, que los datos devueltos son reales y no un fixture de staging — y el paso de no-duplicación al reintentar una acción de ruta encolada.

### Cargo & Tracking — cargas y confirmación de entrega

- **Endpoint correcto:** la URL sale de configuración de Production, nunca hardcodeada ni reutilizada de Staging. Capa 2: se abre un envío conocido de producción y se confirma que ID, estado y eventos corresponden a datos reales de ese ambiente — no basta con que "la pantalla funcione".
- **Nada sensible en logs:** nunca tokens/API keys/headers `Authorization`, direcciones completas, datos personales del remitente/destinatario, payloads completos, ni credenciales de servicios externos. Permitido: `ShipmentId`/`TrackingId`, código HTTP, nombre de la operación, tiempo de respuesta, `correlation id` — solo si no exponen información personal por sí solos.
- **Prueba mínima (gate explícito):** abrir envío existente y ver tracking#/estado/eventos; modo avión con la pantalla de tracking abierta no cierra ni deja en blanco la app; reconectar solo Wi-Fi refresca desde producción sin reiniciar la app; una actualización real de estado aparece sin duplicarse ni alterar el orden; cerrar/reabrir mantiene consistencia con el backend. **Si falla la recuperación tras perder red, aparecen eventos duplicados, se muestra información de otro envío, o el estado del dispositivo no coincide con producción, no sale el release** — es la parte más crítica porque es exactamente lo que el conductor va a necesitar operando sin conexión estable.
- Esta es la entrega más rigurosa de las 4 en la prueba de comportamiento real — sirve de referencia.

### Billing & Escrow — estado de pago

- **Endpoint correcto:** se confirma que Billing apunta al endpoint de producción del servicio de pagos (ej. `api-pagos.ecotrace.com`), nunca a Sandbox o desarrollo (ej. `sandbox-pagos.ecotrace.com`, `dev-pagos.ecotrace.com`). Se revisa la configuración de Release/Producción confirmando la cadena completa: Billing → API de Pagos → Producción.
- **Nada sensible en logs:** nunca número completo de tarjeta, CVV, tokens de pago, API keys, secretos ni datos bancarios completos — ejemplo explícito de lo prohibido: `"payment_token": "sk_live_xxxxxxxxxxxxx"`. Para debugging, solo información anonimizada: `payment_id`, `transaction_id`, `status`.
- **Prueba mínima (gate explícito, "Smoke Test extremo"):** login → aparece un pago pendiente → modo avión → confirmar una acción offline → aparece "Pendiente de sincronizar" → cerrar/reabrir la app → la operación persiste → reconectar → la cola sincroniza → pasa a "Sincronizado" → **repetir la sincronización o forzar un reintento y confirmar que no se genera un pago duplicado**. Prueba extra: simular que el servidor rechaza la operación al reconectar y confirmar que pasa a estado "Rechazada" (no se pierde ni se marca como exitosa).
- Billing y Cargo & Tracking son los dos equipos que, de forma independiente, diseñaron la prueba de no-duplicación — la razón por la que se adopta como estándar transversal arriba.

---

## Esquema mínimo de log seguro (síntesis de los 4 equipos)

Los 4 equipos coinciden en la prohibición (tokens, credenciales, secretos, datos personales completos) pero divergían en qué SÍ se permite registrar. Se estandariza un esquema mínimo común, reutilizable por los 4 módulos, que cada uno complementa con sus propios campos no sensibles (`ShipmentId` en Cargo & Tracking, `payment_id` en Billing, etc.):

```
{ CorrelationId, EntityId (no-PII), HttpStatus, OperationName, DurationMs }
```

Nunca: contraseñas, JWT/refresh tokens completos, claves de firma, números de tarjeta/CVV/tokens de pago, direcciones o datos de contacto completos, coordenadas GPS en crudo sin agregar.

---

## Consecuencias

- Los 4 módulos verifican el endpoint de producción en dos capas (configuración + comportamiento real), no solo en una — cerrando el riesgo que dejaba abierto el diseño original de Fleet Management.
- La prueba de no-duplicación al reintentar una sincronización es obligatoria en los 4 módulos, validando en producción que el `OperationId` del ADR 0005 cumple su propósito.
- Todo checklist de release declara explícitamente qué pasos bloquean la publicación — ningún checklist queda como lista de buenas intenciones.
- Se adopta un esquema mínimo común de log seguro, con el criterio de exclusión (qué nunca se registra) unificado entre los 4 módulos.
- El riesgo asimétrico señalado por Billing —que una app mal configurada ejecute operaciones reales desde un contexto que debería ser de prueba— queda documentado como principio general aplicable a cualquier módulo que module dinero o estado inmutable, no solo a Billing.

**No quedan pendientes abiertos de esta clase, ni del semestre.** Las dos correcciones de consolidación (verificación en dos capas y prueba de no-duplicación) quedaron resueltas y documentadas arriba, con la justificación de por qué se hizo cada cambio. Con este ADR se cierra el ciclo completo de EcoTrace B2B: de la arquitectura (ADR 0001) a la app publicada en manos del conductor (ADR 0006).
