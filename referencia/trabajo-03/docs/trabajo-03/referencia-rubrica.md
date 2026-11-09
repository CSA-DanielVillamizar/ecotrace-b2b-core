# Trabajo 3: la referencia, criterio por criterio

Este documento dice dónde está en la referencia cada punto de la rúbrica del Issue #25 y cómo comprobarlo. Sirve para que un escuadrón vea cómo se ve un criterio «Completo» en código y en pruebas, y para que el docente corrija con la misma vara.

Es una solución posible, no la única. Se evalúa que la parte de cada escuadrón cumpla el contrato de la especificación, no que se parezca a este código.

## Cómo comprobarla

```bash
cd referencia/trabajo-03
dotnet build EcoTrace.sln -c Release      # sin advertencias
dotnet test  EcoTrace.sln -c Release      # 141 pruebas de Mobile.Core + 208 del servidor
pwsh scripts/verificar-release.ps1 -LevantarProduccion   # capas 1 y 2 de la verificación de release
```

La app MAUI no está en `EcoTrace.sln` (necesita el workload de Android). Se abre con `EcoTrace.Mobile.sln`.

## Seguridad: tokens (1.0)

| Qué pide la rúbrica | Dónde está | Qué lo demuestra |
|---|---|---|
| Identity firma RS256 de verdad, con la clave fuera del repositorio | `src/EcoTrace.Identity/Api/Security/ClavesDeFirma.cs`, `EmisorDeTokens.cs` | `IdentityAutenticacionTests` (claims, `alg`, `kid`, duración de 15 minutos) |
| Publica la clave pública con su `kid` (RFC 7638) | `AuthController.cs` y la ruta `/.well-known/jwks.json` | `ClavesPublicasDeIdentityTests` |
| El refresh token se guarda con hash y se revoca | `src/EcoTrace.Identity/Domain/Credenciales.cs` (`RefreshToken`), `AuthController.cs` | `IdentityAutenticacionTests` (hash en la base, logout, suspendida) |
| Valida firma, `exp`, `iss` y `aud`, sin llamar a Identity en cada solicitud | `Api/Security/SeguridadRecursos.cs` en Fleet, Cargo y Billing | `ClavesPublicasDeIdentityTests`: el JWKS se baja una vez y se guarda; si Identity cae, un token válido sigue pasando |
| `401` genérico frente a `403` | `SeguridadIdentity.cs` y `SeguridadRecursos.cs` (`RespuestasDeSeguridad`) | `AtaquesAResourceServersTests`: sin token, vencido, firma alterada en un byte, `alg: none`, otro `iss`, otro `aud`, otra clave, HS256 con la clave pública → `401`; otra organización o rol sin permiso → `403` |
| Llamadas entre servicios con token de servicio | `Api/Security/TokenDeServicio.cs` en Cargo y Billing | `IdentityAutenticacionTests` (rol `Servicio`, una sola audiencia, 5 minutos, se niega si el secreto o la audiencia no corresponden) y `TokensDeServicioTests` (cada llamada del Saga y del Outbox lleva su token, se reutiliza, y si Identity no lo entrega el Saga falla cerrado sin mover dinero) |

## Móvil: offline-first (1.5)

| Qué pide la rúbrica | Dónde está | Qué lo demuestra |
|---|---|---|
| Caché visible con banner | `Mobile.Core/Modulos/Cache.cs` (`LectorConCache`), `Conexion/BannerSinConexion.cs` | `SesionYCacheTests`, `ClasificadorYModulosTests` (texto y hora de la última sincronización) |
| Acción encolada con `OperationId` generado en el dispositivo | `Mobile.Core/Cola/ColaLocalSqlite.cs` | `ColaLocalTests` (estados `PendienteSync`, `Sincronizando`, `Sincronizado`, `Rechazado`; sobrevive a un cierre) |
| Sincronización en orden, con la sesión vigente | `Mobile.Core/Sincronizacion/MotorSincronizacion.cs` | `MotorSincronizacionTests` (orden, el `OperationId` viaja como `Idempotency-Key`, un reintento reenvía la misma clave, sin token vigente no se envía nada, un rechazo no bloquea la cola) |
| El servidor la procesa una sola vez | `Api/Extensions/Idempotencia.cs` en los cuatro servicios: la clave y el efecto se guardan en la misma transacción | `RutasParaLaAppTests`: repetir la clave devuelve `200` con el resultado anterior; cinco reintentos a la vez; otra persona o ruta con la misma clave no la reutiliza |

## Conflictos tipificados (0.5)

| Tipo | Quién lo produce | Cómo se muestra |
|---|---|---|
| `A`, se puede corregir | Validación de dominio y `400` del framework en `ApiDefaults.cs`; ejemplo: teléfono con formato inválido | La tarjeta ofrece «Corregir y reintentar» (`App/Servicios/Correcciones.cs`, `Controles/ListaOperacionesView.xaml`): el dato vuelve al formulario y se envía como acción nueva |
| `B`, decide el servidor | `DomainException.Conflict` (carga ya entregada, vehículo reservado, pago ya liberado) | `Rechazada · Conflicto B`, con el motivo; la acción se conserva |
| `C`, edición concurrente | `GuardarAsync` convierte `DbUpdateConcurrencyException` en `409` con `tipoConflicto: "C"` | `Rechazada · Conflicto C`, pide revisión, sin botón de corregir y sin reintento automático |

Pruebas: `RutasParaLaAppTests` (A y B de cada módulo), `ConflictoCDeConcurrenciaTests` (el choque devuelve `409` tipo C, sin dejar huella, y la misma clave pasa después), `ClasificadorYModulosTests` (la app clasifica la respuesta).

## Seguridad en el dispositivo (0.5)

| Qué pide la rúbrica | Dónde está | Qué lo demuestra |
|---|---|---|
| El JWT vive en `SecureStorage`, nunca en SQLite | `App/Servicios/Seguridad.cs` (`IAlmacenSeguro`), `Mobile.Core/Sesion/Sesion.cs` | `SesionYCacheTests` |
| El login nunca se encola | `ServicioDeSesion.IniciarSesionAsync` | `SesionYCacheTests` |
| Sesión degradada: 48 horas desde el último contacto y sin acciones de dinero | `PoliticaDeSesion` en `Sesion.cs`; desbloqueo en `App/Platforms/Android/AutenticadorLocalAndroid.cs` | `SesionYCacheTests` (reglas) y prueba en dispositivo con el PIN del dispositivo (ver `docs/release/identity.md`) |

## Despliegue y release (1.0)

| Qué pide la rúbrica | Dónde está |
|---|---|
| Checklist de dos capas con gate explícito | `docs/release/` (uno común y uno por módulo) y `scripts/verificar-release.ps1` |
| Capa 2: comportamiento real contra el entorno de producción | `GET /api/_meta/entorno` en cada servicio, el registro con identificador fijo que Identity siembra solo en producción, y el cruce de tokens entre entornos; todo lo ejecuta el script |
| Registros sin datos sensibles | `ApiDefaults.cs` (esquema `OperationName`, `HttpStatus`, `DurationMs`, `EntityId`, más `CorrelationId`); `RegistrosSegurosTests` captura los registros reales y busca contraseñas y tokens |
| Firma de Release distinta de la de Debug | `verificar-release.ps1` compara con `apksigner` y descarta `CN=Android Debug` |
| El pipeline o su salida local como evidencia | `.github/workflows/app-release.yml` (la verificación corre sola; el job que compila y firma el APK es manual) |

## Integración y PR (0.5)

La integración se mira en el ensayo del 10 de noviembre y en la demo. Lo que la referencia aporta como ejemplo es la cadena completa funcionando: login → app sin conexión → sincronización → Cargo → Saga → pago liberado (`SagaLiberacionPagoTests`, `FlujoEntreContextosTests` y la prueba en el emulador descrita en `referencia-fase2.md`). El CI está en `.github/workflows/referencia-trabajo-03.yml`.

## Lo que esta referencia no demuestra

- La compilación para `net8.0-android`: se compiló para `net10.0-android` porque el equipo donde se hizo no tiene el workload de .NET 8.
- La huella dactilar. Se probó el cuadro del sistema con el PIN del dispositivo.
- La regla de las 48 horas con el reloj real: se prueba la lógica, no el paso del tiempo.
- `docker compose` ni la ejecución del flujo en GitHub Actions.
- Un conflicto `B` por reasignación de conductor: no hay una ruta para reasignar una carga ya asignada, así que ese caso solo lo cubren las pruebas del servidor.
- Registros con el esquema mínimo en la app móvil: lo verificado es el de los servidores.
