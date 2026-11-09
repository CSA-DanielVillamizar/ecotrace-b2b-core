# Referencia del Trabajo 3 · Fase 3: despliegue y release

Corresponde al criterio **«Despliegue y release»** (1.0) y a la parte de registros seguros del ADR 0006. El detalle de lo probado, módulo por módulo, está en [`docs/release/`](../release/README.md); aquí se explica cómo está armado y cómo usarlo.

## Los dos entornos

Hay desarrollo y **producción simulada**: otro conjunto de servicios en otros puertos, con otra clave de firma y otro emisor de tokens.

| | Desarrollo | Producción simulada |
|---|---|---|
| Puertos | 5101 a 5104 | 6101 a 6104 |
| `iss` de los tokens | `ecotrace-identity-desarrollo` | `ecotrace-identity-produccion` |
| Clave de firma | Se genera sola en `App_Data` | Sale de `Jwt__PrivateKeyPem`; sin ella el servicio no arranca |
| Configuración de la app | `Configuracion/appsettings.Desarrollo.json` (Debug) | `Configuracion/appsettings.Produccion.json` (Release) |

La app elige la configuración al compilar (`EcoTrace.App.csproj`): Debug incrusta la de desarrollo y Release la de producción. No hay direcciones escritas en el código.

## `GET /api/_meta/entorno`

Cada servicio responde en qué entorno corre, su versión y el emisor que reconoce:

```json
{ "servicio": "Billing", "entorno": "produccion", "version": "1.0.0", "emisor": "ecotrace-identity-produccion" }
```

No lleva secretos. Es lo que permite comprobar el entorno real sin fiarse de una variable. Está en `Api/Extensions/ApiDefaults.cs` de cada servicio y lo prueba `EntornoTests`.

Identity siembra, **solo en producción**, un registro con identificador fijo (una organización de verificación, definida en `appsettings.Production.json`). Si la app o el script apuntan por error a desarrollo, ese registro no existe y la verificación falla.

## `scripts/verificar-release.ps1`

```bash
pwsh scripts/verificar-release.ps1 -LevantarProduccion -Apk <ruta-al-apk>
```

Termina con código `1` si falla un paso que bloquea y con `0` si todo pasa. Lo que no pudo comprobar lo lista como **omitido**; no lo da por bueno.

**Capa 1, configuración:** el build Release incrusta la configuración de producción; las URLs no son las de desarrollo ni usan sus puertos; no hay direcciones en el código; el emisor de producción difiere del de desarrollo en los cuatro servicios; no hay claves ni secretos en los archivos de producción; `Mobile.Core` en Release tiene las optimizaciones activas; y, si se entrega el APK, está firmado con una clave que no es la de Debug y no es depurable.

**Capa 2, comportamiento real:** cada servicio dice que es producción; el administrador de producción inicia sesión con un token de producción; el registro de identificador fijo existe; un token de producción lo aceptan Fleet, Cargo y Billing; un token con el emisor de desarrollo recibe `401`; y, si hay un desarrollo corriendo, un token **real** de desarrollo recibe `401` en producción y el registro fijo no existe en desarrollo.

Con `-LevantarProduccion` el script arranca la producción simulada por su cuenta (clave y secretos aleatorios, bases temporales) y la detiene al terminar. `-DejarCorriendo` la deja en pie para probar la app de Release contra ella, y `-ClaveDeAdministrador` fija la contraseña del administrador de esa producción.

Se comprobó que el script bloquea de verdad: con la URL de producción de la app cambiada a un puerto de desarrollo termina en `1`, y con servicios de desarrollo ocupando los puertos de producción también.

## Registros seguros

Cada solicitud deja un registro con el esquema mínimo del ADR 0006:

```
Users.ActualizarPerfil -> 200 en 336 ms (entidad 6820e090-…)
```

con las propiedades `OperationName`, `HttpStatus`, `DurationMs`, `EntityId` y, por el middleware de correlación, `CorrelationId`. `OperationName` es el nombre de la acción y no la dirección con datos. No se registran cuerpos ni el encabezado `Authorization`.

`RegistrosSegurosTests` y `RegistrosSegurosEnLosRecursosTests` capturan los registros reales de los cuatro servicios con un sumidero de Serilog y comprueban que tienen el esquema y que ninguno contiene la contraseña, el refresh token, el token válido, uno forjado ni el correo completo.

Un detalle que costó encontrarlo: cada servicio escribe en su propio logger (`preserveStaticLogger: true`). Con el logger estático de Serilog, dos servicios en el mismo proceso, como en las pruebas, se pisan los sumideros.

## Firma del APK

```bash
dotnet build src/EcoTrace.App -c Release -p:AndroidPackageFormat=apk \
  -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=<keystore> \
  -p:AndroidSigningKeyAlias=<alias> \
  -p:AndroidSigningKeyPass=env:CLAVE -p:AndroidSigningStorePass=env:CLAVE
apksigner verify --print-certs <apk>
```

El keystore de Release **no se sube al repositorio** (`*.keystore` y `*.jks` están en `.gitignore`). La evidencia es la salida de `apksigner` con una huella SHA-256 distinta a la del keystore de Debug. En el flujo de GitHub, el keystore y su clave salen de secretos del repositorio.

## Flujo `.github/workflows/app-release.yml`

- `verificacion`: compila en Release, corre las pruebas de `Mobile.Core` y ejecuta el script con `-LevantarProduccion`. Corre solo en los PR.
- `app-android`: instala el workload de MAUI, compila y firma el APK y vuelve a ejecutar el script con `-Apk`. Es manual (`workflow_dispatch`) porque necesita el SDK de Android.

**No se ejecutó en GitHub Actions.** Su lógica se probó ejecutando cada paso en local. Si un job no puede correr en la nube, la salida del mismo flujo en local pegada en el PR vale como evidencia.

## Cómo se ve «Completo» en el checklist de un módulo

Cada `docs/release/<módulo>.md` tiene los pasos con una columna **Bloquea** (sí o no) y el resultado real de cada uno, con las salvedades dichas en la misma fila. Un paso que no se pudo comprobar se escribe como no probado; no se marca como cumplido.
