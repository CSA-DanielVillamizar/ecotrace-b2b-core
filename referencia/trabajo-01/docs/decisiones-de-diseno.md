# Decisiones de diseño

Por qué la solución de referencia está hecha así. Cada decisión dice qué se hizo, la razón y lo que cuesta. Al final hay una lista de lo que se dejó fuera a propósito y otra de las diferencias con el [ADR 0001](../../../docs/adr/0001-monolito-vs-microservicios.md).

## 1. Tres proyectos por contexto y ningún repositorio

Cada contexto tiene `Domain`, `Infrastructure` y `Api`. `Domain` no tiene paquetes ni referencias a otros proyectos. `Infrastructure` solo conoce su propio `Domain`. Los controladores usan el `DbContext` directamente.

La lógica de negocio vive en las entidades (`Carga.RegistrarSeguimiento`, `Pago.Liberar`), así que un repositorio sobre EF Core solo reenviaría llamadas. Una capa de aplicación se justifica cuando hay que coordinar varios pasos, y eso empieza en el Trabajo 2 con el Saga.

Costo: los controladores conocen EF Core. Si la consulta de un endpoint crece, conviene moverla a una clase propia.

## 2. Ningún proyecto compartido

No existe un `Common`, un `Shared` ni un `BuildingBlocks`. El código transversal (configuración de la API, manejo de errores, lectura del modelo de EF) está copiado en los cuatro proyectos `Api`, con el mismo contenido salvo el namespace.

Un proyecto referenciado por los cuatro obliga a compilar y desplegar juntos lo que se separó para desplegarse por separado. Además, los proyectos compartidos tienden a acumular entidades y DTOs, que es justo lo que la regla del ADR 0001 prohíbe. La prueba `Ningun_proyecto_referencia_a_un_proyecto_de_otro_modulo` falla si aparece uno.

Costo: un cambio transversal se hace en cuatro archivos. Si eso llega a doler, la salida es un paquete NuGet versionado, no una referencia de proyecto.

## 3. `[ReferenciaExterna]` declara cada identificador que cruza un contexto

Todo `Guid` que apunta a una entidad de otro contexto lleva el atributo con el contexto de destino y una descripción:

```csharp
[ReferenciaExterna("Identity", "Transportista dueño de la flota")]
public Guid TenantId { get; private set; }
```

La regla "solo por ID" es fácil de romper sin darse cuenta: basta una propiedad de navegación o una clave foránea. Con el atributo, las pruebas de arquitectura exigen tres cosas: que todo identificador que no sea clave propia ni clave foránea interna esté declarado, que ninguno declarado sea clave foránea, y que ninguno apunte a su propio contexto. El endpoint `/api/_meta/contexto` publica esa información y la consola dibuja el mapa de contextos a partir de ella.

Se comprobó que las pruebas detectan la violación: se rompió la regla a propósito de dos maneras (una referencia de proyecto de Fleet Management a Identity, y un `TenantId` sin atributo) y en ambos casos fallaron con un mensaje que nombra el proyecto o la propiedad.

## 4. Una base SQLite por contexto

`identity.db`, `fleet.db`, `cargotracking.db` y `billing.db`, cada una en la carpeta `App_Data` de su API. La separación física es la prueba más directa de que ningún contexto mira la base de otro.

La cadena de conexión sale de configuración (`ConnectionStrings:Default`). Las pruebas la reemplazan por un archivo temporal y Docker la deja en un volumen.

Limitaciones: el proveedor de SQLite de EF Core no traduce ordenamientos ni agregados sobre `decimal`, así que ninguna consulta los usa y las sumas de dinero las hace la consola. SQLite serializa las escrituras; con el volumen de esta referencia no se nota.

## 5. El `TenantId` llega en el cuerpo de la solicitud

El ADR 0001 dice que el `TenantId` viaja en el JWT. Todavía no hay JWT (es el Trabajo 3), así que cada solicitud declara el `TenantId` y los servicios lo aceptan sin verificarlo. Hoy cualquiera puede escribir con cualquier `TenantId`. Es una limitación conocida, no un descuido.

Cuando llegue la autenticación, el valor se leerá del claim y saldrá del cuerpo de la solicitud. El cambio queda acotado a los contratos y los controladores.

## 6. Los servicios no validan referencias a otros contextos

Fleet Management no comprueba que el `TenantId` exista en Identity. Cargo & Tracking no comprueba que el vehículo asignado pertenezca al transportista. Billing no comprueba que la carga esté entregada antes de crear el pago.

Comprobarlo exigiría llamar al otro servicio o leer su base de datos, y el ADR 0001 lo prohíbe. La coordinación entre contextos es el tema de los ADR 0002 y 0003 (Trabajo 2). La consecuencia es que pueden existir identificadores que no apuntan a nada.

Dos pruebas dejan esto explícito. `Guarda_los_identificadores_externos_sin_validarlos_contra_identity` crea un vehículo con un `TenantId` inventado. `Del_alta_de_organizaciones_a_la_liberacion_del_pago_solo_con_identificadores` recorre el ciclo completo y apaga Identity a la mitad: los otros tres servicios siguen funcionando.

## 7. Validación en dos capas y errores como ProblemDetails

Los contratos usan `[Required]` para la presencia de campos y producen un 400 con los errores por campo. El dominio valida las reglas (longitudes, formatos, estados) y lanza `DomainException` con uno de tres tipos: validación (400), conflicto (409) o no encontrado (404). Un `IExceptionHandler` los convierte en `ProblemDetails` (RFC 9457).

Las violaciones de unicidad y los choques de concurrencia de SQLite se traducen a 409 en `GuardarAsync`, dentro de `Infrastructure`, para que la capa `Api` no conozca detalles de SQLite. Los rechazos esperados se registran como información; solo los fallos inesperados salen como error.

## 8. El dinero: concurrencia, auditoría y unicidad

`EstadoEscrow` es un token de concurrencia. Si dos solicitudes intentan liberar el mismo pago a la vez, una gana y la otra recibe 409. La prueba `Dos_liberaciones_simultaneas_nunca_mueven_el_dinero_dos_veces` lanza las dos en paralelo y verifica un 200, un 409 y exactamente dos filas de auditoría.

La auditoría es de solo agregado: el constructor es interno y ningún endpoint la modifica. Una carga admite un solo pago y una sola factura (índices únicos). Los montos deben ser mayores que cero y tener como máximo dos decimales.

## 9. Máquina de estados de la carga

```
Pendiente → Asignado → EnTransito ⇄ ConNovedad → Entregado
```

`Carga` es la raíz del agregado y solo cambia de estado a través de sus métodos. `TransicionPermitida` es la tabla de transiciones; cualquier otra devuelve 409 con el mensaje de qué estados sí se permiten. Una novedad exige una nota. La consola solo ofrece transiciones válidas, pero la autoridad es el servidor.

## Diferencias con el ADR 0001

- **`Pendiente`.** El ADR lista cuatro estados de seguimiento (Asignado, En tránsito, Con novedad, Entregado). Se agregó `Pendiente` como estado inicial, porque hay que representar una carga recién creada que todavía no tiene vehículo.
- **`Role` y `RoleClaim`.** El ADR menciona "Role/Claim". Se modelaron como entidades, con los cuatro roles y sus permisos sembrados por la migración.
- **`RegistradoPorUserId`.** El ADR dice que Fleet Management referencia el `UserId` "del coordinador que gestiona el registro". Por eso `Conductor` y `Vehiculo` guardan quién los registró. `Conductor` también tiene un `UserId` opcional para vincular la cuenta del propio conductor, que necesitará la app móvil del ADR 0005.
- **`Factura`.** El ADR la lista en Billing, así que está incluida.

## Migraciones y arranque

Cada contexto tiene una migración `InitialCreate`. `dotnet-ef` 8 está fijado como herramienta local (`.config/dotnet-tools.json`), así que `dotnet tool restore` basta para regenerarlas. Las migraciones se aplican al arrancar la API (`Database:MigrarAlArrancar`), que es cómodo en local. En producción convendría aplicarlas desde el pipeline de despliegue (ADR 0006) y no desde la aplicación.

## La consola compone datos por ID en el navegador

La consola pide cada lista al servicio dueño y después resuelve nombres buscando por identificador: la placa de un vehículo asignado a una carga, el nombre de una organización. Es composición del lado del cliente. No hay gateway ni BFF; elegir entre esas opciones es parte del Trabajo 2.

## Pruebas

Hay tres tipos. Las de integración levantan cada API completa con `WebApplicationFactory` y una base temporal, y la ejercitan por HTTP. Las de arquitectura leen los `.csproj` y el modelo de EF Core para hacer cumplir la regla de oro. Una prueba de flujo recorre los cuatro contextos usando solo identificadores.

## Fuera de alcance

Lo siguiente no está y no debería esperarse en el Trabajo 1:

- Autenticación y autorización (Trabajo 3, ADR 0004).
- Comunicación entre servicios, mensajería y Saga (Trabajo 2, ADR 0002 y 0003).
- Paginación: las listas devuelven todo.
- Observabilidad más allá de los registros de Serilog.
- Healthchecks de Docker (la imagen base no incluye `curl`).
- Una versión vectorial del logotipo con la tipografía convertida a curvas (ver la guía de marca).
