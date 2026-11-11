# Referencia del Trabajo 3 · Fase 1: seguridad en el servidor

Es la solución de referencia de la parte de seguridad
(ADR 0004): Identity como Authorization Server, los tres Resource Servers, los tokens de servicio entre módulos y las pruebas
de ataque. Corresponde al checkpoint 1 del jueves 15 de octubre y al criterio **«Seguridad: tokens»** (1.0 punto) de la rúbrica.

Falta, para las fases siguientes: `Idempotency-Key` y `tipoConflicto` en los servicios, las rutas nuevas
(`/api/vehiculos/{id}/novedades`, `/api/pagos/{id}/evidencias`), la app móvil con inicio de sesión y `SecureStorage`, y el
despliegue (`/api/_meta/entorno`, `verificar-release.ps1`, `app-release.yml`, firma).

## Cómo se ve funcionando

```bash
scripts/run-all.sh      # o run-all.ps1: compila y levanta los cuatro servicios y la consola
```

La consola (<http://localhost:5100>) pide iniciar sesión. En desarrollo, el administrador de la plataforma es
`admin@ecotrace.local` y su contraseña está en `src/EcoTrace.Identity/Api/appsettings.Development.json`. «Cargar datos de
ejemplo» crea cuatro organizaciones y cinco personas con contraseña y hace cada paso con **el token de la persona que
corresponde**: el supervisor registra la flota y asigna, el administrador del generador pide la carga y paga, y el conductor
confirma la entrega, que dispara el Saga.

Para ver un token a mano:

```bash
curl -s -X POST http://localhost:5101/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@ecotrace.local","password":"<la de appsettings.Development.json>"}'
curl -s http://localhost:5101/.well-known/jwks.json
```

## Qué se decidió (y por qué)

| Decisión | Razón |
|---|---|
| **RS256 con la clave privada solo en Identity.** Sale de `Jwt:PrivateKeyPem` (PEM o Base64 del PEM). Sin ella, en desarrollo se genera y se guarda en `App_Data/` (ignorada por git); fuera de desarrollo el servicio **no arranca**. | La clave nunca está en el repositorio, y un servicio de autenticación mal configurado no debe quedar en pie. |
| **El `kid` es la huella RFC 7638 de la clave pública.** | Cambia solo cuando cambia la clave, y los módulos saben cuándo volver a bajar el JWKS. |
| **Los Resource Servers usan `ConfigurationManager` con un recuperador propio del JWKS.** Se baja una vez, se renueva cada hora o cuando llega un `kid` desconocido, con un mínimo de 30 s entre renovaciones forzadas. | «Local, sin red»: si Identity cae, un token válido sigue validándose con la copia en memoria. El mínimo evita que alguien con tokens de `kid` inventados haga que el módulo bombardee a Identity. |
| **Se valida firma, `exp`, `iss` y `aud`**, con `ValidAlgorithms = RS256` y `RequireSignedTokens`. Tolerancia de reloj de 30 s. | `alg: none` y la confusión de algoritmos quedan fuera. |
| **El `401` es genérico** («No autenticado»); el motivo exacto va solo al registro, sin el token. El `403` también es genérico. | El cliente no aprende por qué falló un token. |
| **Contraseñas con PBKDF2-SHA256** de la biblioteca base, con sal e iteraciones guardadas junto al hash. Iteraciones configurables (1000 en pruebas). El inicio de sesión falla siempre con el mismo mensaje y verifica un hash ficticio si la cuenta no existe. | Sin paquetes en el dominio, y sin delatar por tiempo qué cuentas existen. |
| **Refresh token de 32 bytes aleatorios; en la base solo su SHA-256.** Vence a los 7 días, se revoca con `logout` (idempotente) y una organización suspendida deja de poder renovar. Sin rotación. | La especificación pide un access token nuevo por refresh, no un refresh nuevo. |
| **Token de servicio:** rol `Servicio`, **un solo módulo de destino** (`aud`), 5 minutos, sin `tenant_id`. Cada cliente (`billing`, `cargo`) tiene su secreto y las audiencias que puede pedir. Quien llama lo guarda hasta 60 s antes de que venza. | Un token robado de una llamada no sirve contra otro módulo, y no se pide uno por cada llamada. |
| **Si no hay token de servicio, la llamada no sale.** El Saga falla cerrado (no mueve dinero) y el Outbox deja el mensaje pendiente. | Sin identidad no se llama. |
| **El `tenant_id` sale del token.** Si el cuerpo trae una organización o un `registradoPorUserId` distinto del token, `403`. Las listas se filtran con la organización del token, y un token sin organización (el de un servicio) no lista nada. | «Nunca de un parámetro del cliente». El filtro «por ninguna organización» habría mostrado todo: era un hueco real. |
| **Claim adicional `plataforma=true` y `TenantType.Plataforma`** para la organización que opera la plataforma. Su administrador (el **operador**) crea organizaciones, las suspende, y **lee** todo, pero no escribe datos de otras organizaciones: no mueve dinero de un pago ajeno. | Hace falta alguien que cree la primera organización, y no debe ser un superusuario de los datos. Está documentado como permiso adicional, que la especificación permite. |
| **Las rutas internas** (`/api/reservas`, `/api/liberaciones`, `/api/autorizaciones-pago`, `/api/eventos/*`) son solo del rol `Servicio`; ni siquiera el operador entra. | Es la regla de la especificación. |
| **El administrador de plataforma se siembra desde configuración** (`Plataforma:AdminEmail` y `AdminPassword`). | Es el único camino para crear al primer administrador. |

### Diferencias con la especificación

- **Claim `plataforma`** y **`TenantType.Plataforma`**: no están en la especificación; ver arriba.
- **El operador lee todo.** La matriz mínima dice que las listas se filtran con el `tenant_id` del token; el operador es la excepción documentada.
- **Contraseña obligatoria:** `POST /api/users` exige una contraseña de al menos 10 caracteres. Antes del Trabajo 3 no había contraseñas, y las cuentas anteriores sin contraseña **no pueden iniciar sesión**.
- **La consola ya no reserva vehículo y conductor.** Es una llamada entre servicios (Billing a Fleet, con token de servicio) y un navegador no tiene ese secreto. Fleet libera de forma idempotente, así que el Saga completa igual; lo que se pierde es ver el vehículo pasar de `Reservado` a `Disponible` en la demostración.

## Mapa de la rúbrica «Seguridad: tokens» (1.0)

| Lo que pide | Dónde está | Qué lo demuestra |
|---|---|---|
| Emite RS256 con los claims del ADR y publica la clave | `Identity/Api/Security/{ClavesDeFirma,EmisorDeTokens}.cs`, `Controllers/AuthController.cs` | `IdentityAutenticacionTests`: claims, RS256, `kid`, 15 minutos, el JWKS valida el token y no trae datos privados |
| Contraseña y refresh nunca en claro | `Identity/Domain/Credenciales.cs`, `Infrastructure/Configurations/RefreshTokenConfiguration.cs` | `La_contrasena_y_el_refresh_token_nunca_se_guardan_en_claro` |
| Refresh, logout idempotente, suspensión | `AuthController` | `Refresh_cambia_...`, `Cerrar_sesion_revoca_...`, `Un_refresh_vencido_...`, `Una_organizacion_suspendida_...` |
| Valida firma, `exp`, `iss` y `aud` sin llamar a Identity en cada solicitud | `*/Api/Security/SeguridadRecursos.cs` (Fleet, Cargo y Billing, repetido a propósito) | `AtaquesA{Fleet,Cargo,Billing}Tests`; `ClavesPublicasDeIdentityTests` (una sola descarga, Identity caído, rotación de clave, `kid` inventados) |
| `401` y `403` distinguidos | Eventos en `RespuestasDeSeguridad`; `[Authorize(...)]` y comprobaciones por organización en los controladores | Los ocho ataques de la especificación, en cada Resource Server |
| Tokens de servicio entre módulos | `Billing` y `CargoTracking` `/Api/Security/TokenDeServicio.cs` | `TokensDeServicioTests`: audiencia única, 5 minutos, reutilización, fallo cerrado, Outbox pendiente |

### Las pruebas de ataque (en cada Resource Server)

Sin token, vencido, firma alterada, `alg: none`, `iss` de otro entorno, `aud` de otro módulo, firmado con otra clave diciendo
ser de Identity, `kid` inventado (todos `401`); token válido de otra organización y token válido con un rol sin permiso
(`403`); el `401` no delata el motivo; las listas se filtran con el token y no con lo que pida el cliente; un token de servicio
no lista datos; las rutas internas son solo del rol `Servicio`; `/health` y `/api/_meta/contexto` siguen abiertas.

**Se comprobó que las pruebas detectan algo:** al debilitar a propósito Fleet (`ValidateAudience`, `RequireSignedTokens`,
`ValidateIssuer` y `ValidateLifetime` en falso) fallaron seis de las dieciséis pruebas de ataque, una por cada control roto.

## Qué conviene decir en clase el jueves 15

- Un token firmado con **otra clave pero el mismo `kid`** y con los claims perfectos se rechaza: lo único falso es la firma.
- Por qué `alg: none` no pasa: `RequireSignedTokens` y la lista de algoritmos válidos.
- Por qué el `401` es genérico y dónde queda el motivo: el registro del servidor, sin el token.
- El error más fácil de cometer: filtrar una lista con el `tenant_id` del token cuando el token **no trae** `tenant_id`. Devuelve todo.
- Un token de servicio con una sola audiencia: qué pasa si alguien lo roba.

## Qué se verificó y qué no

**Verificado**

- 174 pruebas de integración del servidor (89 del Trabajo 2 adaptadas a tokens, 26 de autenticación de Identity, 50 de ataque,
  5 del JWKS y 4 de tokens de servicio) y 103 del `Mobile.Core`, todas en verde en Windows.
- La plataforma real, con los cinco procesos y HTTP de verdad: inicio de sesión, `401` sin token y con un token alterado,
  `200` con el token del operador, el JWKS publicado y el preflight de CORS con `Authorization`.
- La consola en el navegador: pantalla de inicio de sesión, la sesión del operador, «Cargar datos de ejemplo» completo
  (cuatro organizaciones, cinco personas, un pago liberado por el Saga con tokens de servicio reales), «Preparar envío de
  demostración» y «Registrar entrega» con el Saga completado en 4 de 4 pasos.

**No verificado**

- `docker compose up`: se actualizó el archivo y su YAML es válido, pero el demonio de Docker no respondió y no se levantó.
- Linux ni el CI de GitHub Actions: no se ejecutó con esta rama.
- El build de Release ni la configuración de producción (`appsettings.Production.json` solo trae el emisor).
- Nada contra HTTPS: todo corre en `http` local.
- No hay límite de intentos de inicio de sesión ni bloqueo de cuentas: es una defensa real que **no** se implementó.
- La consola guarda en `sessionStorage` las credenciales de las personas de demostración que ella misma crea. Es aceptable para
  usuarios de usar y tirar en desarrollo, y es lo que una aplicación real nunca debe hacer.
- No es una auditoría de seguridad: cubre los ocho ataques de la especificación y algunos más, no un análisis completo.
