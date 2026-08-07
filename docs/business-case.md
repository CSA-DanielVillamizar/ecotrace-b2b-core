# Caso de Negocio — EcoTrace B2B

**Clase 0 · PD174-3 · Programación Distribuida · ITM**
Documento producido siguiendo Specification-Driven Development (SDD). Cierra el [Issue #1](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/1).

---

## 1. Problema

Las normativas ESG (*Environmental, Social and Governance*) obligan a las empresas generadoras de residuos industriales a certificar la trazabilidad completa de su disposición final. Al mismo tiempo, los transportistas pyme que prestan ese servicio esperan entre 30 y 90 días para recibir su pago, sin visibilidad ni garantía sobre cuándo se liberarán los fondos.

Sin trazabilidad verificable ni liquidez oportuna, el ciclo logístico-ambiental se vuelve insostenible tanto para el regulador como para el transportista.

## 2. Solución

**EcoTrace B2B** es un SaaS distribuido que conecta generadores de carga con flotas de transporte. La plataforma:

- Traza rutas y emite certificados de disposición inmutables y auditables.
- Ejecuta pagos condicionados (Factoring/Escrow) al confirmar la entrega vía app móvil.
- Da a cada actor (generador, transportista, auditor, entidad financiera) visibilidad en tiempo real sobre el estado de su operación.

**Métrica de éxito:** reducir el tiempo de pago al transportista de 60 días a menos de 24 horas tras la entrega confirmada.

## 3. Actores / Stakeholders

| Actor | Interés en el sistema |
|---|---|
| Generador de carga | Certificar cumplimiento ESG y contratar transporte confiable |
| Transportista pyme | Cobrar rápido y con trazabilidad de su servicio |
| Auditor ESG | Verificar cumplimiento normativo sin depender de reportes manuales |
| Entidad financiera | Ejecutar Factoring/Escrow sobre transacciones verificadas |

## 4. Módulos y Dueños

| Módulo | Responsabilidad | Dueño(s) según rol del equipo |
|---|---|---|
| **Identity** | Autenticación y autorización (OAuth2/JWT) de todos los actores | Jorge Armando Perez — Infra y Ciberseguridad |
| **Fleet Management** | Gestión de flotas, conductores y disponibilidad de vehículos | Juan Esteban Coneo, Yohan Esneider Granda, Maria Camila Sarmiento — Backend Core |
| **Cargo & Tracking** | Trazabilidad de rutas y certificados de entrega inmutables | Juan de Dios Sanchez — Redes y Conectividad (con soporte del Escuadrón Móvil para telemetría) |
| **Billing** | Pagos condicionados (Factoring/Escrow) y auditoría financiera | Yordys Alfonso Leudo, Maria Alejandra Rua — Procesos ERP B2B |

Revisión técnica general: Santiago Martinez (Líder Técnico). Responsable de Estrategia y Producto: John Sebastian Gomez.

---

## 5. Historias de Usuario Iniciales (formato SDD)

### 5.1 Identity

**Como** generador de carga,
**Quiero** autenticarme con mi cuenta corporativa mediante OAuth2,
**Para** acceder de forma segura a mis certificados de disposición sin compartir credenciales con terceros.

- **Dado que** tengo una cuenta corporativa registrada en el sistema,
- **Cuando** inicio sesión con mis credenciales OAuth2,
- **Entonces** el sistema me otorga un token JWT válido por 8 horas con mis permisos de generador de carga.

### 5.2 Fleet Management

**Como** transportista pyme,
**Quiero** registrar la disponibilidad de mis vehículos en tiempo real,
**Para** que los generadores de carga puedan asignarme rutas apenas tenga capacidad libre.

- **Dado que** tengo al menos un vehículo registrado en mi flota,
- **Cuando** actualizo su estado a "disponible" desde la app móvil,
- **Entonces** el vehículo aparece en menos de 5 segundos en el listado de flotas disponibles para asignación.

### 5.3 Cargo & Tracking

**Como** auditor ESG,
**Quiero** consultar el historial completo de rutas y certificados de un envío,
**Para** verificar el cumplimiento normativo sin depender de reportes manuales.

- **Dado que** un envío fue marcado como entregado,
- **Cuando** el auditor consulta el certificado de trazabilidad por su número de guía,
- **Entonces** el sistema muestra la ruta completa, las firmas digitales y el timestamp de cada punto de control, sin posibilidad de edición posterior.

### 5.4 Billing

**Como** transportista pyme,
**Quiero** que el pago se libere automáticamente al confirmar la entrega en la app móvil,
**Para** no depender de ciclos administrativos de 60 días que afectan mi flujo de caja.

- **Dado que** el certificado de entrega fue firmado digitalmente por el receptor de carga,
- **Cuando** el sistema valida la firma contra el contrato de Escrow,
- **Entonces** el pago se libera en menos de 24 horas y se emite un certificado inmutable de la transacción.

---

## 6. Criterios de Aceptación del Issue #1

- [x] Este documento describe Problema, Solución, Módulos y Actores.
- [x] Se redactaron 4 Historias de Usuario en formato SDD (Como/Quiero/Para + Criterios de Aceptación Gherkin).
- [x] Cada módulo (Identity, Fleet, Cargo & Tracking, Billing) tiene un dueño asignado según el rol del equipo.
