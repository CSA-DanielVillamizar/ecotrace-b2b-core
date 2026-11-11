# Referencia del Trabajo 3 · Fase 2: lo que la app necesita (servidor y móvil)

Corresponde al checkpoint 2 del jueves 29 de octubre y a los criterios **«Móvil: offline-first»** (1.5),
**«Conflictos tipificados»** (0.5) y **«Seguridad en el dispositivo»** (0.5) de la rúbrica.

## Servidor

| Pieza | Dónde | Qué dice |
|---|---|---|
| **`tipoConflicto` A, B o C** en el `ProblemDetails` | `DomainException.Tipo` en los cuatro módulos; `DomainExceptionHandler`; `ApiDefaults` (400 de validación del framework) | Una validación es **A**; un conflicto de estado es **B** por defecto; un choque de concurrencia (`DbUpdateConcurrencyException`) es **C**. Un 404 no lleva tipo. |
| **`Idempotency-Key`** | `Api/Extensions/Idempotencia.cs` (filtro `[Idempotente]`, repetido en cada módulo), tabla `OperacionesIdempotentes` | La misma clave de la misma persona en la misma ruta responde **200 con el resultado anterior** y el encabezado `Idempotent-Replayed`; el efecto y la clave se guardan en **una sola transacción**. Otro contenido con la misma clave: `422`. Un rechazo no deja huella. Sin el encabezado la ruta funciona como siempre. |
| **Fleet:** `POST /api/vehiculos/{id}/novedades` | `VehiculosController`, `NovedadVehiculo` | Conflicto **B** si el vehículo se reservó para otra carga mientras no había señal. |
| **Billing:** `POST /api/pagos/{id}/evidencias` | `PagosController`, `EvidenciaEntrega` | Conflicto **B** si el pago ya salió de la custodia («ya fue liberado por otro proceso»). Queda en la auditoría. |
| **Cargo:** seguimiento idempotente y `conductorId` opcional | `CargasController.RegistrarSeguimiento` | Conflicto **B** si la carga se reasignó a otro conductor. |
| **Identity:** `PUT /api/users/{id}/perfil` (teléfono) | `UsersController`, `User.ActualizarPerfil` | **A** si el teléfono es inválido; **B** si la organización se suspendió; solo la propia cuenta. |

Pruebas: `RutasParaLaAppTests` (21): repetir la clave, cinco reintentos a la vez, otro contenido, clave de otra persona o de otra ruta, rechazo sin huella,
clave inválida, y cada conflicto A y B de cada módulo.

**Límite conocido:** los candados que ordenan dos solicitudes simultáneas con la misma clave son de un solo proceso. Con varias instancias del
servicio, la restricción única `(Clave, Alcance)` de la base es la que decide. Las claves guardadas no se purgan.

## Móvil (`Mobile.Core` y `EcoTrace.App`)

| Pieza | Dónde | Qué dice |
|---|---|---|
| **Sesión** | `Sesion/ServicioDeSesion.cs` | Inicia sesión en Identity (necesita conexión y **nunca se encola**), guarda los tokens en `IAlmacenSeguro` (**SecureStorage** en la app, nunca SQLite), renueva con el refresh token y **no devuelve un token vencido**: sin conexión devuelve `null` y las acciones esperan. |
| **Sesión degradada** | `PoliticaDeSesion` | Token vencido y sin conexión: hasta **48 h desde el último contacto real con Identity**; hay que desbloquear con huella o PIN (`IAutenticadorLocal`); **no se pueden encolar acciones críticas** (confirmar una entrega). Pasadas las 48 h, expira. |
| **Caché** | `LectorConCache`, tabla `CacheDeDatos` | La pantalla muestra lo último del servidor o lo guardado **con su fecha**, y dice por qué. |
| **Acciones por módulo** | `Modulos/*/…Handler.cs` | Identity: `ActualizarPerfil`; Cargo: `ConfirmarEntrega` (**crítica**); Fleet: `ReportarNovedad`; Billing: `RegistrarEvidencia`. |
| **Pantallas** | `Modulos/*/*Page.xaml`, `ViewModels/ModulosViewModels.cs` | Perfil (login, sesión, teléfono, cerrar), Cargas, Vehículo, Pagos: cada una con su caché, su acción y la cola del módulo. |

Pruebas: 33 nuevas en `SesionYCacheTests` (136 en total en `Mobile.Core`).


## Qué se probó en un dispositivo y qué no

Probado el 17 de octubre de 2026 en un emulador Android 11 (API 30, 720×1280) contra la plataforma real corriendo en local, con la app compilada
para `net10.0-android`. Las capturas y los datos de la prueba no se publican.

| Prueba | Resultado |
|---|---|
| Inicio de sesión del conductor sembrado | Funciona. Identity responde 200 y la sesión se restaura desde `SecureStorage` al cerrar y abrir la app. |
| Lista de cargas con la sesión vigente | Llega desde Cargo con el token firmado. |
| Modo avión | La lista sigue visible desde la caché. El banner dice «Sin conexión. Mostrando datos guardados. Última sincronización: 5:12 p. m.». |
| Acción crítica con la sesión vencida y sin conexión | La app la bloquea y explica por qué. No se encola. |
| «Confirmar entrega» sin conexión con el token vigente | Queda `En cola`, sobrevive a cerrar la app a la fuerza y se sincroniza sola al volver la señal. |
| Efecto en el servidor | Un solo seguimiento `Entregado` y el pago pasa a `Liberado` por el Saga, unos 9 segundos después. |
| **No duplicación** | Se devolvió la operación sincronizada a `PendienteSync` en la base del teléfono. La app la reenvió con la misma `Idempotency-Key`; Cargo respondió 200 («se devuelve el resultado anterior»), no hubo un segundo `Entregado` ni un segundo pago liberado, y la operación volvió a `Sincronizado` con 2 intentos. |
| **Conflicto B** | Mientras el teléfono estaba sin señal, el supervisor marcó esa carga como entregada por la API. Al volver la señal el servidor respondió 409 de tipo B y la app mostró `Rechazada · Conflicto B · el servidor decidió; la acción se conserva`, con el motivo. |
| **Conflicto A: corregir y reintentar** | Se guardó un teléfono vacío. El servidor respondió 400 de tipo A con la regla en español («El teléfono debe tener entre 7 y 15 dígitos…») y la acción quedó `Rechazada · Conflicto A`. Solo en este tipo la tarjeta ofrece **«Corregir y reintentar»**: el botón lleva el dato rechazado al formulario de Perfil, se corrige, y «Guardar teléfono» crea una acción nueva (`ReintentarCorregidaAsync`). Esa se sincronizó y el teléfono quedó en `+573124567890`. La acción rechazada se conserva en la cola. |
| **Conflicto C** | **Con un Fleet simulado.** Una carrera de concurrencia real no se repite a voluntad, así que un script de apoyo, que no forma parte del repositorio, puso en el puerto de Fleet un servidor que responde 409 con la misma forma de `ProblemDetails` que genera el servidor real. La app mostró `Rechazada · Conflicto C · otra persona cambió lo mismo; pide revisión`, **sin** botón de corregir, y envió la acción una sola vez: no la reintenta sola. Lo que el servidor real hace ante el choque lo prueba `ConflictoCDeConcurrenciaTests` (409 con `tipoConflicto` «C», sin dejar huella, y la misma clave pasa cuando el choque ya no ocurre). |
| **Las tres acciones restantes, sin conexión** | Con el token vigente y en modo avión se encolaron `Actualizar perfil` (Identity), `Reportar novedad` (Fleet) y `Registrar evidencia` (Billing). Al volver la señal la app las sincronizó (En cola 0, Enviadas 4, Rechazadas 1) y cada ruta se llamó una sola vez: Identity guardó el teléfono normalizado (`+573115550199`), Fleet guardó la novedad con el usuario del conductor y Billing registró «Evidencia de entrega registrada» en la auditoría del pago, que sigue `EnCustodia`. |

Defectos que salieron de esta prueba y ya están corregidos, con prueba automática: el banner decía «Aún no hay una sincronización» con la lista llena (ahora un dato
que llega del servidor cuenta como sincronización), el aviso de sesión vencida seguía en pantalla tras reconectar, el texto «Datos guardados del hoy», el motivo de un 400 de validación del framework en inglés (ahora dice «Hay datos que no cumplen las reglas» y qué campos revisar) y la falta del camino «corregir y reintentar» que pide la especificación para el tipo A.

**No probado todavía:**

- Un conflicto B por **reasignación de conductor**. La referencia no tiene una ruta para reasignar una carga ya asignada, así que ese caso solo está cubierto por las pruebas del servidor (`RutasParaLaAppTests`). El conflicto B de la prueba fue el de una entrega que otra persona ya había registrado.
- Un conflicto C **real** en el dispositivo: se vio con una respuesta simulada del servidor. El servidor real lo produce y está probado, pero no se midió la carrera de extremo a extremo.
- El desbloqueo con huella o PIN de la sesión degradada (`BiometricPrompt`) y la regla de las 48 horas en un dispositivo.
- El build de Release firmado y la compilación contra `net8.0-android`.
- El flujo `app-release.yml` en GitHub Actions y `docker compose`.
