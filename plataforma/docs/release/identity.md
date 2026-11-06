# Checklist de release: Identity (sesión)

Módulo de sesión de la app y servicio Identity, según el [ADR 0006](../../../docs/adr/0006-despliegue-smartphones.md) y la [especificación del Trabajo 3](../trabajo-03/especificacion.md#despliegue-adr-0006). Cada fila dice qué se comprobó, si bloquea el release y el resultado real, con sus salvedades en la misma fila.

**Entorno de la prueba:** 5 de noviembre de 2026. Producción simulada de Identity levantada con `scripts/levantar-produccion.ps1` (http://localhost:6101, `ASPNETCORE_ENVIRONMENT=Production`, emisor `https://identity.ecotrace.local/produccion`, clave y secretos en `%LOCALAPPDATA%\EcoTrace\produccion`, base `identity-produccion.db`). App: **build de Release** (`dotnet build -c Release -f net8.0-android -t:Run`) en un emulador Pixel con Android 14 (API 34), con PIN de pantalla.

## Capa 1: configuración

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 1 | El build es Release y la app incrusta `Configuracion/appsettings.Produccion.json` | Sí | Cumple. La pantalla de inicio de la app dice «Entorno: producción». `verificar-release.ps1` comprueba la condición del `.csproj`. |
| 2 | La configuración de producción de la app no apunta a desarrollo | Sí | Cumple: `entorno=produccion`, Identity en `http://10.0.2.2:6101` (no 5101). |
| 3 | Identity en producción tiene su emisor, no genera claves y no expone Swagger | Sí | Cumple: `appsettings.Production.json` con `Emisor=…/produccion`, `GenerarClaveSiFalta=false`, `Swagger:Habilitado=false`. Sin clave, el servicio no arranca. |
| 4 | Ninguna clave privada ni secreto de producción en el repositorio | Sí | Cumple: `git grep` no encuentra `BEGIN PRIVATE KEY` ni ninguno de los secretos de producción en archivos versionados. Los secretos de servicio están en el repo solo como SHA-256 de los de **desarrollo**. |

## Capa 2: comportamiento real

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 5 | `GET /api/_meta/entorno` del servicio que responde dice `produccion` | Sí | Cumple: `{"servicio":"identity","entorno":"produccion","version":"0.3.0"}`. Contra desarrollo (5101) responde `desarrollo` y el script **bloquea** (ver abajo). |
| 6 | El login en producción emite un token con el emisor de producción | Sí | Cumple: `iss=https://identity.ecotrace.local/produccion`. |
| 7 | El dato conocido de producción existe: organización de verificación con id fijo `0e7ace00-0000-4000-8000-0000000000a1` | Sí | Cumple: «Verificación de producción». En desarrollo ese id responde `404` (prueba `El_dato_conocido_de_produccion_no_existe_en_desarrollo`). |
| 8 | Un token de desarrollo no sirve en producción | No | Cumple: `401` (otro emisor y otra clave). No bloquea porque necesita desarrollo levantado; si no está, el script lo omite y lo dice. |

## Prueba mínima del módulo en el dispositivo (build de Release contra producción)

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 9 | Inicio de sesión desde la app contra producción | Sí | Cumple: el conductor de verificación entra y la pestaña Perfil muestra «Sesión activa», «Conductor de verificación», «Verificación de producción · Activo». |
| 10 | Los tokens no están en SQLite ni en `Preferences` en texto plano | Sí | Cumple, inspeccionado en el dispositivo con `adb root`: `grep -c eyJ` (inicio de todo JWT) sobre `files/ecotrace-cola.db3*` da **0**, y sobre `shared_prefs/*.xml` da **0**, igual que `refreshToken`. `SecureStorage` los guarda cifrados con el Keystore. |
| 11 | Cerrar la app a la fuerza y reabrirla conserva la sesión | Sí | Cumple: tras `am force-stop` y reabrir, Perfil muestra «Sesión activa» sin pedir clave. |
| 12 | Modo avión: la pantalla carga desde lo guardado | Sí | Cumple: banner «Sin conexión. Mostrando datos guardados. Última sincronización: 10:01 p. m.» y el perfil visible. |
| 13 | Acción encolada sin conexión (`PendienteSync`) que sobrevive a cerrar la app | Sí | Cumple: cambio de teléfono → «Cambio guardado en el teléfono. Se envía cuando haya señal.»; tras `force-stop` y reabrir, la pantalla de inicio muestra **1 en cola** con «Actualizar perfil · En cola». |
| 14 | Al reconectar se sincroniza y el servidor lo guarda | Sí | Cumple: sin modo avión, la acción pasa a `Sincronizada` y `GET /api/perfil` en **producción** devuelve `telefono=3152223344`. |
| 15 | No duplicación: un reintento con el mismo `OperationId` no repite el efecto | Sí | Cumple con pruebas automáticas, **no forzado desde el teléfono**: `AppIdentityDePuntaAPuntaTests.Si_se_pierde_la_respuesta_el_reenvio_no_repite_el_cambio_ni_pisa_uno_mas_nuevo` usa el motor y la cola reales de la app contra Identity real, pierde la respuesta a propósito y comprueba que el reenvío no pisa un cambio más nuevo; `PerfilIdentityTests` cubre la clave repetida y la simultánea. |
| 16 | Un rechazo pasa a `Rechazado` con su motivo y tipo (conflicto A) | Sí | Cumple en Release: teléfono `12ab` → «Conflicto A · el dato se puede corregir y reintentar», «El teléfono debe tener entre 7 y 15 dígitos, con un + opcional al inicio.», botón «Corregir y reintentar». |
| 17 | Conflicto B (organización suspendida) en pantalla | No | Cubierto por `AppIdentityDePuntaAPuntaTests.Si_suspenden_la_organizacion_mientras_no_hay_senal_el_cambio_vuelve_como_conflicto_B`. **No probado en el dispositivo** en esta corrida. |
| 18 | Sesión degradada: token vencido sin conexión pide huella o PIN y deja encolar | Sí | Cumple, probado en el emulador con el build de **Debug** (ver [identity.md](../trabajo-03/identity.md#sesión-degradada-sin-conexión-y-con-el-token-vencido)): «Sin conexión · bloqueada» → cuadro del sistema (`BiometricPrompt`) → PIN → «limitada hasta» 48 h → el cambio se encola y llega con el token renovado. **Huella no probada** (el emulador no tiene sensor; la especificación acepta el PIN). |

## Registros seguros

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 19 | Cada solicitud deja un registro con `{ CorrelationId, EntityId, HttpStatus, OperationName, DurationMs }` | Sí | Cumple en el servidor. Ejemplo real: `[22:03:59 INF] Identity \| 9ead0145000e4dd3afae8ae41c4a66ca \| Auth.Login -> 200 en 221 ms (entidad 1740da15-de18-4113-a324-4b3aac29c51e)`. `EntityId` es siempre un identificador (Guid o `servicio:<cliente>`), nunca un correo. |
| 20 | Ningún registro lleva contraseñas, JWT, refresh tokens, secretos ni datos de contacto | Sí | Cumple: `DespliegueIdentityTests.Cada_solicitud_deja_un_registro_con_el_esquema_minimo_y_sin_datos_sensibles` captura los registros reales de login correcto y fallido, refresh, token de servicio, token forjado y cambio de perfil, y comprueba que no aparecen la contraseña (buena ni mala), el access token ni su firma, el refresh token, el token de servicio, el secreto del cliente, el correo ni el teléfono. Revisado también en la salida de consola de una corrida real. La app móvil no escribe registros con este esquema: lo verificado es el servidor. |

**Gate:** bloquean el release los pasos 1 a 7, 9 a 16 y 18 a 20. **Todos cumplen**, con las salvedades dichas en su fila: el paso 15 se demostró con pruebas automáticas y no forzando el reintento desde el teléfono; el 18 se probó con el build de Debug y con el PIN, no con huella.

## Evidencia: `scripts/verificar-release.ps1`

Contra la producción simulada (termina con código 0):

```text
Capa   Paso                                                                   Bloquea Resultado Detalle
Capa 1 La app en Release incrusta appsettings.Produccion.json                    True Cumple    Release -> Configuracion/appsettings.Produccion.json
Capa 1 La configuración de producción de la app no apunta a desarrollo           True Cumple    entorno=produccion, identity=http://10.0.2.2:6101
Capa 1 Identity en producción: emisor propio, sin claves generadas ni Swagger    True Cumple    iss=https://identity.ecotrace.local/produccion
Capa 1 Ninguna clave privada ni secreto de producción en el repositorio          True Cumple    Sin claves ni secretos de producción en archivos versionados
Capa 2 GET http://localhost:6101/api/_meta/entorno responde produccion           True Cumple    servicio=identity, entorno=produccion, version=0.3.0
Capa 2 El login de producción emite un token con el emisor de producción         True Cumple    iss=https://identity.ecotrace.local/produccion
Capa 2 El dato conocido de producción existe (organización de verificación)      True Cumple    Verificación de producción (0e7ace00-0000-4000-8000-0000000000a1)
Capa 2 El conductor de verificación inicia sesión en producción                  True Cumple    role=Conductor, tenant_id=0e7ace00-0000-4000-8000-0000000000a1
Capa 2 Un token de desarrollo no sirve en producción                            False Cumple    401 (otro emisor y otra clave)

Release aprobado: 9 pasos, ninguno bloqueante falló.
```

Apuntando por error a desarrollo (`-Identity http://localhost:5101`), **bloquea** (termina con código 1):

```text
Capa 2 GET http://localhost:5101/api/_meta/entorno responde produccion           True FALLA     El servicio que responde es de 'desarrollo'
Capa 2 El login de producción emite un token con el emisor de producción         True FALLA     The remote server returned an error: (401) Unauthorized.
Capa 2 El dato conocido de producción existe (organización de verificación)      True FALLA     Sin token de producción.
Capa 2 El conductor de verificación inicia sesión en producción                  True FALLA     The remote server returned an error: (401) Unauthorized.

RELEASE BLOQUEADO: 4 paso(s) bloqueante(s) fallaron.
```

## Lo que falta y no es de este módulo

- **Firma del APK de Release con una clave distinta de la de Debug** (`apksigner verify --print-certs`) y el flujo `.github/workflows/app-release.yml`: son piezas compartidas de los cuatro escuadrones. El APK de esta prueba se firmó con la clave de Debug del SDK.
- Los pasos de los demás servicios en `verificar-release.ps1` y en `levantar-produccion.ps1` (cada escuadrón agrega los suyos).
