# EcoTrace B2B · Solución de referencia del Trabajo 3

La plataforma del Trabajo 2 con la cadena de confianza del Trabajo 3 encima: **login con JWT, app móvil sin conexión, sincronización, Cargo, Saga y pago liberado**, más el release. Implementa lo decidido en los [ADR 0004](../../docs/adr/0004-oauth2-jwt.md), [0005](../../docs/adr/0005-offline-first-maui.md) y [0006](../../docs/adr/0006-despliegue-smartphones.md) con el alcance del [Issue #25](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/25) y la [especificación](docs/trabajo-03/especificacion.md).

La construyó el docente para calibrar la rúbrica del Trabajo 3. Se publica con el trabajo todavía en curso, como material de consulta, y no cambia las reglas:

- **No es la única forma válida de resolverlo.** La rúbrica evalúa que la parte de cada escuadrón cumpla el contrato de la especificación, no que se parezca a este código.
- **La sustentación individual sigue siendo sobre lo que cada persona escribió.** Se revisa con `git blame`, y se pregunta por qué se diseñó así, conectando con el ADR.
- **No toca la base de ustedes.** Está aislada en esta carpeta. `plataforma/` y los PR de los escuadrones no cambian.

## Cómo ejecutarla

Requiere el SDK de .NET 8.

```powershell
# Windows (PowerShell)
.\scripts\run-all.ps1
```

```bash
# macOS, Linux o Git Bash
./scripts/run-all.sh
```

Después se abre <http://localhost:5100>, se inicia sesión con el administrador de desarrollo (está en `src/EcoTrace.Identity/Api/appsettings.Development.json`) y se pulsa **Cargar datos de ejemplo**.

| Servicio | Puerto | Swagger |
|---|---|---|
| Consola web | 5100 | |
| Identity | 5101 | <http://localhost:5101/swagger> |
| Fleet Management | 5102 | <http://localhost:5102/swagger> |
| Cargo & Tracking | 5103 | <http://localhost:5103/swagger> |
| Billing & Escrow | 5104 | <http://localhost:5104/swagger> |

```bash
dotnet build EcoTrace.sln -c Release      # sin advertencias
dotnet test  EcoTrace.sln -c Release      # 141 pruebas de Mobile.Core y 206 del servidor
pwsh scripts/verificar-release.ps1 -LevantarProduccion   # capas 1 y 2 de la verificación de release
```

La app MAUI no está en `EcoTrace.sln` porque necesita el workload de Android. Se abre con `EcoTrace.Mobile.sln`. Para probarla contra la plataforma que corre en local, `scripts/sembrar-movil.py` crea una organización, un conductor con contraseña, un vehículo, cargas en tránsito y un pago en custodia, e imprime con qué cuenta entrar.

## Dónde está cada criterio de la rúbrica

[`docs/trabajo-03/referencia-rubrica.md`](docs/trabajo-03/referencia-rubrica.md) dice, criterio por criterio, en qué archivos está y qué prueba lo demuestra.

| Criterio | Puntos | Resumen | Detalle |
|---|---|---|---|
| Seguridad: tokens | 1.0 | Identity firma RS256, publica el JWKS y guarda el refresh con hash; los tres Resource Servers validan firma, `exp`, `iss` y `aud` sin llamar a Identity; pruebas de ataque. | [fase 1](docs/trabajo-03/referencia-fase1.md) |
| Móvil: offline-first | 1.5 | Caché con banner, cola local con `OperationId`, sincronización en orden y `Idempotency-Key` en el servidor. | [fase 2](docs/trabajo-03/referencia-fase2.md) |
| Conflictos tipificados | 0.5 | `tipoConflicto` A, B o C en el servidor; la app los muestra con su motivo y deja corregir y reintentar el tipo A. | [fase 2](docs/trabajo-03/referencia-fase2.md) |
| Seguridad en el dispositivo | 0.5 | Tokens en `SecureStorage`, login que no se encola y sesión degradada con huella o PIN, 48 horas y sin acciones de dinero. | [fase 2](docs/trabajo-03/referencia-fase2.md) |
| Despliegue y release | 1.0 | `/api/_meta/entorno`, emisor distinto por entorno, `verificar-release.ps1` de dos capas, registros seguros y firma de Release. | [fase 3](docs/trabajo-03/referencia-fase3.md) y [`docs/release/`](docs/release/README.md) |
| Integración y PR | 0.5 | La cadena completa con el servicio real de cada módulo y el flujo de CI. | [`referencia-rubrica.md`](docs/trabajo-03/referencia-rubrica.md) |

## Qué hay en la carpeta

```
referencia/trabajo-03/
├── src/
│   ├── EcoTrace.Identity/ · FleetManagement/ · CargoTracking/ · Billing/   Domain · Infrastructure · Api
│   ├── EcoTrace.Console/            Consola web, con inicio de sesión
│   ├── EcoTrace.Mobile.Core/        Cola, sincronización, caché, sesión y banner (sin MAUI)
│   └── EcoTrace.App/                La app del conductor (.NET MAUI, Android) y un módulo por servicio
├── tests/
│   ├── EcoTrace.Tests/              Integración, ataques a los tokens, idempotencia, conflictos y registros seguros
│   └── EcoTrace.Mobile.Tests/       Cola, motor de sincronización, sesión y caché
├── docs/
│   ├── trabajo-03/                  Especificación, esqueleto de la app y la referencia fase por fase
│   └── release/                     Checklist de release común y uno por módulo, con resultados reales
├── scripts/                         run-all, sembrar-movil.py y verificar-release.ps1
└── docker-compose.yml · Dockerfile · global.json · Directory.*.props
```

## Qué se probó y qué no

**Probado:** las dos suites completas (en Windows y en un contenedor Linux con el SDK 8.0.427), la plataforma real con los cinco procesos, y la app de Release firmada en un emulador Android 11 contra la plataforma real: login, caché sin conexión, una acción encolada que sobrevive a cerrar la app, sincronización, no duplicación, conflictos A y B, las cuatro acciones de los módulos y el desbloqueo de la sesión degradada con el PIN del dispositivo. El detalle, con sus salvedades, está en [`docs/release/`](docs/release/README.md).

**No probado:**

- La compilación para `net8.0-android`: la app se compiló para `net10.0-android` porque el equipo donde se hizo no tiene el workload de .NET 8. Con el SDK 8 hace falta `dotnet workload install maui-android`.
- La huella dactilar real: el emulador no tiene sensor y se probó el cuadro del sistema con el PIN.
- El conflicto `C` en un dispositivo con una carrera real: se vio con una respuesta simulada del servidor; el servidor real lo produce y está probado (`ConflictoCDeConcurrenciaTests`).
- El conflicto `B` por reasignación de conductor: no hay una ruta para reasignar una carga ya asignada.
- `docker compose` con esta versión.
- El job manual de `.github/workflows/referencia-trabajo-03.yml` que compila y firma el APK en GitHub.
- Los registros con el esquema mínimo en la app móvil: lo verificado es el de los servicios.
