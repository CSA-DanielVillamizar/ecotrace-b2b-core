# Checklist de release: lo que comparten los cuatro módulos

Solución de referencia del Trabajo 3. Los resultados son de la prueba del **17 de octubre de 2026**.

## Qué se probó

| | |
|---|---|
| Build | `Release`, compilado para `net10.0-android` (el workload de .NET 8 no está en esta máquina; `net8.0-android` **no se compiló**) |
| Dispositivo | Emulador Android 11 (API 30), 720×1280, sin Google Play |
| Backend | La producción simulada que levanta `scripts/verificar-release.ps1` (puertos 6101 a 6104, otra clave de firma, otro `iss`), con el desarrollo corriendo al lado en 5101 a 5104 |
| APK | `co.edu.itm.ecotrace.conductor`, versión 0.1.0, 29,7 MB, no depurable |
| Firma | `CN=EcoTrace Referencia Privada, O=ITM, C=CO`, SHA-256 `faabcc18112ba2e02b369fc4e38c5b7fd7513cfbae5dc7cf2b40751d58e62bf9`. No es la clave de Debug. El keystore no se sube al repositorio. |

## Verificación en dos capas (`pwsh scripts/verificar-release.ps1 -LevantarProduccion -Apk <apk>`)

Resultado: **17 correctos, 0 fallas que bloquean, 0 omitidos.** Código de salida 0.

**Capa 1, configuración** (bloquean todas):

1. El build Release incrusta `appsettings.Produccion.json` y declara `entorno: produccion`.
2. Las URLs de producción de la app no son las de desarrollo ni usan puertos 5101 a 5104.
3. No hay direcciones escritas en el código de la app fuera de `Configuracion/`.
4. El `iss` de producción es distinto al de desarrollo en los cuatro servicios.
5. No hay claves ni secretos en los archivos de configuración de producción del repositorio.
6. `Mobile.Core` compilado en Release tiene las optimizaciones activas.
7. El APK está firmado con una clave que no es la de Debug (el certificado no es `CN=Android Debug` y su huella no coincide con el keystore de Debug de esta máquina).
8. El APK no declara `android:debuggable`.

**Capa 2, comportamiento real** (bloquean todas):

1. Cada servicio responde `GET /api/_meta/entorno` con `entorno: produccion` y el emisor de producción (cuatro comprobaciones).
2. El administrador de producción inicia sesión y su token trae el `iss` de producción.
3. El registro con identificador fijo (`6f1c0a52-3b7e-4d8a-9c41-0e5a7b2d9f10`) existe en producción.
4. Un token de producción lo aceptan Fleet, Cargo y Billing.
5. Un token con el emisor de desarrollo, firmado con una clave ajena, recibe `401`.
6. Un token **real** de desarrollo recibe `401` en producción, y el registro fijo responde `404` en desarrollo.

Si la app apuntara por error a desarrollo, la pantalla funcionaría igual, pero los pasos 1, 3 y 6 de la capa 2 fallarían y el release no se aprobaría. La pantalla de inicio de la app también lo dice: «Entorno: producción».

Se comprobó que el script bloquea: con la URL de Identity de producción cambiada a un puerto de desarrollo termina en código 1, y con servicios de desarrollo ocupando los puertos de producción también.

## Registros seguros (servidores)

Cada solicitud deja un registro como `Users.ActualizarPerfil -> 200 en 336 ms (entidad <id>)`, con su `CorrelationId` y las propiedades `OperationName`, `HttpStatus`, `DurationMs` y `EntityId`. `RegistrosSegurosTests` y `RegistrosSegurosEnLosRecursosTests` capturan los registros reales de los cuatro servicios y comprueban que tienen ese esquema y que ninguno contiene contraseñas, tokens (el válido y uno forjado), `Bearer ` ni el correo completo.

## Prueba mínima con el build de Release (modo avión, cola, reintento, rechazo)

La hicieron sobre todo Cargo e Identity; el detalle está en el checklist de cada módulo.

| Paso | Resultado en Release |
|---|---|
| Modo avión: la pantalla carga desde SQLite sin quedar en blanco | Cumple. Banner «Sin conexión. Mostrando datos guardados. Última sincronización: 8:07 p. m.» |
| Acción encolada con estado `PendienteSync` y sobrevive a cerrar la app | Cumple. «En cola 1» tras un `force-stop` y reabrir sin conexión. |
| Al reconectar sincroniza y pasa a `Sincronizado` | Cumple. Sin pulsar nada. |
| No duplicación | Cumple, **con una salvedad**: el build de Release no es depurable y no se puede tocar su base para forzar que la app reenvíe. Se reprodujo el envío con la misma `Idempotency-Key` que la app usó (leída de la base de Cargo): `200`, `Idempotent-Replayed: true`, un solo `Entregado`, un solo pago liberado. El reenvío hecho por la propia app se probó con el build de Debug. |
| Un rechazo pasa a `Rechazado` con su motivo | Cumple: `Rechazada · Conflicto A` con «El teléfono debe tener entre 7 y 15 dígitos…». |
| Sesión degradada | Cumple: con el token vencido y sin conexión la app pide desbloquear con huella o PIN; con el PIN del dispositivo desbloquea y deja encolar. |

## Lo que no se verificó

- La compilación contra `net8.0-android`.
- La huella dactilar: el emulador no tiene sensor. Se probó la credencial del dispositivo (PIN) con el mismo cuadro del sistema.
- La regla de las 48 horas con el reloj real (se probó que el token vencido degrada la sesión, no que a las 48 horas expire).
- `docker compose` y la ejecución de `.github/workflows/app-release.yml` en GitHub Actions.
- Los registros seguros **de la app móvil**: la app no escribe registros con el esquema `{ CorrelationId, EntityId, HttpStatus, OperationName, DurationMs }`. Los de los servicios sí se verificaron (ver abajo).
- El checklist con el APK firmado por el pipeline de GitHub (aquí se firmó en local).
