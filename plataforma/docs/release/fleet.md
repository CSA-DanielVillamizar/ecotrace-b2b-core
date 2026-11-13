# Checklist de release: Fleet Management

Lo que se revisa antes de sacar Fleet a producción, según el [ADR 0006](../../../docs/adr/0006-despliegue-smartphones.md) y la [especificación del Trabajo 3](../trabajo-03/especificacion.md#despliegue-adr-0006). Cada paso dice si bloquea el release y qué pasó cuando lo probamos.

**Cuándo y cómo se probó:** 12 de noviembre de 2026, en Windows con el SDK de .NET 8.

- **Identity de producción:** levantado con `scripts/levantar-produccion.ps1` de la rama `feature/25-identity-despliegue`, en `http://localhost:6101`, con emisor `https://identity.ecotrace.local/produccion`.
- **Fleet de producción:** levantado con el perfil `produccion` en Release, en `http://localhost:6102`, con la base `fleet-produccion.db`:

  ```
  dotnet run -c Release --project src/EcoTrace.FleetManagement/Api --launch-profile produccion
  ```

- **Verificación:** `scripts/verificar-release-fleet.ps1`.

## Capa 1: configuración

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 1 | Fleet en producción tiene su propio emisor, toma la llave del Identity de producción (no del 5101), usa otra base y no muestra Swagger | Sí | Cumple: `iss=…/produccion`, JWKS en `localhost:6101`, base `fleet-produccion.db`, `Swagger:Habilitado=false`. |
| 2 | La configuración de producción siembra el vehículo conocido | Sí | Cumple: placa `PRD001`, id `f1ee7000-0000-4000-8000-000000000001`. |
| 3 | La app de producción apunta al Fleet de producción | Sí | Cumple: `fleet=http://10.0.2.2:6102`, no el 5102. |

## Capa 2: comportamiento real

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 4 | `GET /api/_meta/entorno` del Fleet que responde dice `produccion` | Sí | Cumple: `servicio=FleetManagement, entorno=produccion`, versión con el commit. Contra el 5102 dice `desarrollo` y el script bloquea. |
| 5 | El Identity de producción da un token con el emisor de producción | Sí | Cumple: `iss=https://identity.ecotrace.local/produccion`. |
| 6 | El vehículo conocido existe en el Fleet que responde | Sí | Cumple: `PRD001`, `Disponible`. En desarrollo ese vehículo no existe, y además el Fleet de desarrollo rechaza el token de producción con `401`. |
| 7 | Repetir la novedad con la misma `Idempotency-Key` no la duplica | Sí | Cumple: el reintento responde `200` con la misma novedad y la lista sigue con 1. La clave es fija, así que repetir la verificación no llena producción de novedades. |
| 8 | Un token de desarrollo no sirve en el Fleet de producción | No | Cumple: `401`, porque cambian el emisor y la llave. No bloquea porque necesita desarrollo levantado; si no está, el script lo omite y lo dice. |

## Registros seguros

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 9 | Cada solicitud deja un registro con `CorrelationId`, `EntityId`, `HttpStatus`, `OperationName` y `DurationMs` | Sí | Cumple. Línea real de producción: `[19:25:36 INF] FleetManagement \| 15fcc96bdbf0491dbcd5a800fed348f1 \| Solicitud Novedades.Reportar sobre f1ee7000-0000-4000-8000-000000000001: 200 en 16 ms`. El `EntityId` es siempre un id, nunca un nombre. |
| 10 | Ningún registro guarda tokens ni datos de personas | Sí | Cumple: la prueba `RegistrosSegurosFleetTests.Ningun_registro_guarda_el_token_ni_datos_de_personas` captura los registros y comprueba que no aparecen el token, `Bearer`, el nombre ni la licencia del conductor. En el registro real de esta corrida, `eyJ` (el inicio de todo token) y `Bearer` aparecen 0 veces. |

## Prueba mínima en el dispositivo

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 11 | La app en Release compila con la pantalla de Fleet | Sí | Cumple: `dotnet build src/EcoTrace.App -f net8.0-android -c Release` genera el APK `co.edu.itm.ecotrace.conductor-Signed.apk` (workload `maui-android` 8.0.100 y la plataforma Android API 34). |
| 12 | Modo avión: la pantalla de Fleet carga lo guardado; la novedad queda en cola, sobrevive a cerrar la app, se sincroniza al volver la señal y un conflicto B queda `Rechazado` con su motivo | Sí | **No probado en un dispositivo.** Está cubierto con pruebas automáticas que usan la cola y el motor reales de la app contra un Fleet falso (`FleetModuloTests`), y en el servidor con `AppConductorFleetTests`. |

**Nota sobre el dispositivo:** la app compila, pero en el equipo donde se hizo esta prueba no hay un emulador compatible. La única imagen instalada es Android 37 con páginas de 16 KB, y una app de .NET 8 no está preparada para ese tamaño de página. Falta probarla en un emulador con Android 14 (API 34), como hizo Identity. El APK de esta prueba está firmado con la clave de Debug.

## Gate

Bloquean el release los pasos 1 a 7 y 9 a 12. Del 1 al 11 **todos cumplen**. El 12 está demostrado con pruebas automáticas y no en el teléfono; por eso, mientras no se pruebe en un emulador, el release de la app no se da por aprobado.

## Evidencia: `scripts/verificar-release-fleet.ps1`

Contra producción (termina con código 0):

```text
Capa 1 Fleet en producción: emisor propio, llave de Identity de producción, otra base y sin Swagger    True Cumple    iss=https://identity.ecotrace.local/produccion, base=fleet-produccion.db
Capa 1 Fleet en producción siembra el vehículo conocido                                                True Cumple    placa PRD001 (f1ee7000-0000-4000-8000-000000000001)
Capa 1 La app de producción apunta al Fleet de producción                                              True Cumple    fleet=http://10.0.2.2:6102
Capa 2 GET http://localhost:6102/api/_meta/entorno responde produccion                                 True Cumple    servicio=FleetManagement, entorno=produccion, version=1.0.0+f5d6f4b786e61862ad087ef47082bbe5c2ac2e05
Capa 2 Identity de producción emite el token con el emisor de producción                               True Cumple    iss=https://identity.ecotrace.local/produccion
Capa 2 El vehículo conocido existe en el Fleet que responde                                            True Cumple    placa=PRD001, estado=Disponible
Capa 2 Repetir la novedad con la misma Idempotency-Key no la duplica                                   True Cumple    reintento 200, misma novedad 6b198c3e-f648-48a6-9bc3-f20a7a9663b3, 1 novedad(es) antes y después
Capa 2 Un token de desarrollo no sirve en el Fleet de producción                                      False Cumple    401 (otro emisor y otra llave)

Release de Fleet aprobado: 8 pasos, ninguno bloqueante falló.
```

Apuntando por error al Fleet de desarrollo (`-Fleet http://localhost:5102`), **bloquea** (termina con código 1):

```text
Capa 2 GET http://localhost:5102/api/_meta/entorno responde produccion                                 True FALLA     El Fleet que responde es de 'desarrollo'
Capa 2 El vehículo conocido existe en el Fleet que responde                                            True FALLA     Error en el servidor remoto: (401) No autorizado.
Capa 2 Repetir la novedad con la misma Idempotency-Key no la duplica                                   True FALLA     Error en el servidor remoto: (401) No autorizado.

RELEASE DE FLEET BLOQUEADO: 3 paso(s) bloqueante(s) fallaron.
```

## Lo que falta y es compartido

- La **firma del APK de Release con una clave distinta de la de Debug** (`apksigner verify --print-certs`) y el flujo `.github/workflows/app-release.yml`. Son de los cuatro escuadrones.
- Pasar estos pasos a `scripts/verificar-release.ps1` cuando el de Identity esté en `main`. Por ahora van en `verificar-release-fleet.ps1` para no chocar con su rama.
