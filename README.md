# EcoTrace B2B Core 🌍🚛💳

[![.NET Version](https://img.shields.io/badge/.NET-8.0%2F9.0-blue.svg)](https://dotnet.microsoft.com/)
[![Architecture](https://img.shields.io/badge/Architecture-Microservices%20%2F%20Saga-orange.svg)]()
[![Methodology](https://img.shields.io/badge/SDD-Specification%20Driven-green.svg)]()
[![Institution](https://img.shields.io/badge/ITM-PDI74%202026-purple.svg)](https://www.itm.edu.co/)

> **Idiomas / Languages:** [Español](#español) | [English](#english)

---

<a name="español"></a>
## 🇪🇸 Español

### 🏗️ Descripción General
**EcoTrace B2B** es una solución de software distribuida y nativa de la nube, diseñada para resolver desafíos complejos en la cadena de suministro. Conecta a empresas generadoras de residuos industriales (cumpliendo con normativas modernas ESG y de economía circular) con flotas de transporte independientes, automatizando la certificación de cumplimiento, la telemetría en tiempo real y los pagos condicionados mediante *Escrow/Factoring* tras la entrega verificada desde dispositivos móviles.

Desarrollado como el proyecto central de ingeniería para la asignatura **Programación Distribuida (PDI74)** en la *Institución Universitaria ITM*, bajo la supervisión arquitectónica del **Prof. M.Sc.IoT Daniel Andrey Villamizar Araque**.

---

### 🏛️ Arquitectura del Sistema y Dominios
La plataforma sigue una estricta arquitectura de microservicios orientada a dominios, comunicándose mediante patrones síncronos (**gRPC**) y asíncronos (**RabbitMQ / Azure Service Bus**):

* **`Identity Microservice`**: Seguridad distribuida basada en OAuth2, OpenID Connect y JWT con *claims* por roles (Conductores, Supervisores de Bodega, Administradores de Tenant).
* **`Fleet Management`**: Rastreo de activos de transporte, estados de conductores y planificación de rutas.
* **`Cargo & Tracking`**: Ingesta de telemetría en tiempo real desde dispositivos móviles con sincronización *offline-first*.
* **`Billing & Escrow`**: Transacciones distribuidas automatizadas gestionadas a través del **Patrón Saga** (Consistencia Eventual y *rollbacks* compensatorios).

---

### 🛠️ Stack Tecnológico
* **Backend Framework:** .NET 8 / 9 (ASP.NET Core WebAPIs, gRPC)
* **Datos y ORM:** Entity Framework Core (SQL Server / NoSQL)
* **Cliente Móvil:** .NET MAUI (Cliente multiplataforma para operadores de transporte en campo)
* **Infraestructura y DevOps:** Docker, Contenedorización, Serilog (Logging Centralizado), GitHub Projects y CLI (`gh`) para flujo de trabajo GitOps.
* **Metodología:** Specification-Driven Development (SDD)

---

### 👥 Escuadrón de Ingeniería (ITM PDI74-3)
* **Estrategia y Producto:** John Sebastian Gomez
* **Líder Técnico:** Santiago Martinez
* **Infraestructura y Ciberseguridad:** Jorge Armando Perez
* **QA y Resiliencia:** Emannuel Carvajal
* **Procesos ERP y Facturación:** Yordys Alfonso Leudo, Maria Alejandra Rua
* **Redes y Conectividad:** Juan de Dios Sanchez
* **Core Backend:** Juan Esteban Coneo, Yohan Esneider Granda, Maria Camila Sarmiento
* **Escuadrón Móvil:** Sebastian Zuluaga, Luis Miguel Villada, Diego Alejandro Velasquez, Juan Pablo Henao

---

### 🚦 Primeros Pasos
Consulte nuestro [Tablero de Proyectos (Projects Board)](https://github.com/) para revisar los Issues activos, Historias de Usuario y los hitos del Sprint bajo nuestro flujo de trabajo GitOps.

---
---

<a name="english"></a>
## 🇬🇧 English

### 🏗️ Overview
**EcoTrace B2B** is an enterprise-grade distributed SaaS platform designed to solve complex supply chain challenges. It bridges the gap between industrial waste generators (complying with modern ESG and circular economy frameworks) and independent transport fleets, automating compliance certification, real-time telemetry, and conditional escrow/factoring payments upon mobile-verified delivery.

Developed as the core engineering project for the **Distributed Programming (PDI74)** course at *Institución Universitaria ITM*, under the architectural supervision of **Prof. Daniel Andrey Villamizar Araque**.

---

### 🏛️ System Architecture & Domains
The platform follows a strict domain-driven microservices architecture communicating via synchronous (**gRPC**) and asynchronous (**RabbitMQ / Azure Service Bus**) patterns:

* **`Identity Microservice`**: OAuth2, OpenID Connect, and JWT-based distributed security with role-based claims (Drivers, Warehouse Supervisors, Tenant Admins).
* **`Fleet Management`**: Transport asset tracking, driver states, and route planning.
* **`Cargo & Tracking`**: Ingestion of real-time telemetry from mobile devices with offline-first synchronization.
* **`Billing & Escrow`**: Automated distributed transactions managed through the **Saga Pattern** (Eventual Consistency and compensating rollbacks).

---

### 🛠️ Technology Stack
* **Backend Framework:** .NET 8 / 9 (ASP.NET Core WebAPIs, gRPC)
* **Data & ORM:** Entity Framework Core (SQL Server / NoSQL)
* **Mobile Client:** .NET MAUI (Cross-platform client for field transport operators)
* **Infrastructure & DevOps:** Docker, Containerization, Serilog (Centralized Logging), GitHub Projects & CLI (`gh`) for GitOps workflow.
* **Methodology:** Specification-Driven Development (SDD)

---

### 👥 Engineering Squad (ITM PDI74-3)
* **Strategy & Product:** John Sebastian Gomez
* **Technical Lead:** Santiago Martinez
* **Infrastructure & Security:** Jorge Armando Perez
* **QA & Resilience:** Emannuel Carvajal
* **ERP & Billing Processes:** Yordys Alfonso Leudo, Maria Alejandra Rua
* **Networking & Connectivity:** Juan de Dios Sanchez
* **Core Backend:** Juan Esteban Coneo, Yohan Esneider Granda, Maria Camila Sarmiento
* **Mobile Squad:** Sebastian Zuluaga, Luis Miguel Villada, Diego Alejandro Velasquez, Juan Pablo Henao

---

### 🚦 Getting Started
Please refer to our [Projects Board](https://github.com/) to check active Issues, User Stories, and Sprint milestones following our GitOps workflow.
