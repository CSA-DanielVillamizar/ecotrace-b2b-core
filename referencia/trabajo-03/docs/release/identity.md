# Checklist de release: Identity (sesión)

Resultados del 17 de octubre de 2026, con el build de Release contra la producción simulada. Lo común (capas 1 y 2, firma, qué no se verificó) está en [README.md](README.md).

**Acción encolada del módulo:** `Actualizar perfil` (cambio de teléfono, `PUT /api/users/{id}/perfil`).
**Conflictos del módulo:** `A` teléfono con formato inválido, `B` organización suspendida.

| # | Paso | Bloquea | Resultado |
|---|---|---|---|
| 1 | Inicio de sesión contra producción con el build de Release; el token trae el `iss` de producción | Sí | Cumple. «Sesión activa». |
| 2 | El token se guarda en `SecureStorage`, no en SQLite | Sí | Cumple por diseño (`IAlmacenSeguro`); la base de la cola solo guarda acciones y caché de datos. No se inspeccionó el almacén del teléfono: el build de Release no es depurable. |
| 3 | Cerrar la app a la fuerza y reabrirla conserva la sesión | Sí | Cumple: tras `force-stop` la app sincronizó con el token guardado, sin pedir clave. |
| 4 | Sin conexión y con el token vencido la sesión pasa a **degradada**: pide desbloquear antes de encolar | Sí | Cumple: «Abre la pestaña Perfil y desbloquea con tu huella o PIN para seguir sin conexión.» |
| 5 | Desbloqueo con el cuadro del sistema | Sí | Cumple con el PIN del dispositivo: título «EcoTrace», «Confirma que eres tú para seguir sin conexión»; después deja encolar. **Huella no probada** (el emulador no tiene sensor). |
| 6 | Teléfono encolado sin conexión (`PendienteSync`) | Sí | Cumple. |
| 7 | Al reconectar sincroniza y el servidor lo guarda | Sí | Cumple: `Sincronizada` y el teléfono quedó `+573152223344` (normalizado). |
| 8 | Un teléfono inválido pasa a `Rechazado` con su motivo y tipo A | Sí | Cumple en Release: `Rechazada · Conflicto A` con «El teléfono debe tener entre 7 y 15 dígitos, con un + opcional al inicio.» |
| 9 | Conflicto A: corregir y reintentar | No | Cumple, probado con el build de Debug: el botón «Corregir y reintentar» devuelve el dato al formulario y la acción nueva se sincroniza (`+573124567890`). |
| 10 | No duplicación de `PUT /perfil` | Sí | Cubierta por la prueba del servidor (`PerfilDeIdentityTests`). **No** se forzó el reintento desde el teléfono para este módulo; el reintento desde la app se probó con Cargo. |
| 11 | Conflicto B (organización suspendida) en pantalla | No | **No probado en el dispositivo.** Cubierto por la prueba del servidor. |
| 12 | Registros sin contraseñas ni tokens, con el esquema mínimo | Sí | Cumple en el servidor: cada solicitud deja `OperationName -> HttpStatus en DurationMs ms (entidad EntityId)` con su `CorrelationId`, y la prueba captura los registros reales y comprueba que ninguno lleva contraseñas, tokens (válido ni forjado) ni `Bearer `. La prueba de Identity (`RegistrosSegurosTests`) además comprueba que ni la contraseña (la buena y la equivocada), ni el refresh token, ni el correo completo aparecen. |

**Gate:** bloquean el release los pasos 1 a 8, 10 y 12. Todos cumplen, con las salvedades dichas en cada fila (huella no probada; el reintento del paso 10 no se forzó desde el teléfono). La app móvil no escribe registros con este esquema: lo verificado es el servidor.

## Evidencia del servidor (producción simulada)

- `PUT /api/users/{id}/perfil` respondió `400` con tipo A cuando el teléfono llegó vacío y `200` con el teléfono válido.
- La respuesta `400` ahora habla español y dice qué revisar; un teléfono vacío lo valida el dominio (`El teléfono debe tener entre 7 y 15 dígitos…`).
