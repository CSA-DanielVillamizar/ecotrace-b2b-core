# ecotrace-b2b-core
Enterprise-grade distributed SaaS platform for reverse logistics and embedded fintech (ESG compliance, real-time cargo tracking, and automated event-driven Sagas). Developed for ITM Distributed Systems.

# EcoTrace B2B Core 🌍🚛💳

> Enterprise Distributed SaaS Platform for Reverse Logistics & Embedded FinTech.

[![.NET Version](https://img.shields.io/badge/.NET-8.0%2F9.0-blue.svg)](https://dotnet.microsoft.com/)
[![Architecture](https://img.shields.io/badge/Architecture-Microservices%20%2F%20Saga-orange.svg)]()
[![Methodology](https://img.shields.io/badge/SDD-Specification%20Driven-green.svg)]()
[![Institution](https://img.shields.io/badge/ITM-PDI74%202026-purple.svg)](https://www.itm.edu.co/)

---

## 🏗️ Overview
**EcoTrace B2B** is a cloud-native, distributed software solution designed to solve complex supply chain challenges. It bridges the gap between industrial waste generators (complying with modern ESG and circular economy frameworks) and independent transport fleets, automating compliance certification, real-time telemetry, and conditional escrow/factoring payments upon mobile-verified delivery.

Developed as the core engineering project for the **Distributed Programming (PDI74)** course at *Institución Universitaria ITM*, under the architectural supervision of **Prof. Daniel Andrey Villamizar Araque**.

---

## 🏛️ System Architecture & Domains
The platform follows a strict domain-driven microservices architecture communicating via synchronous (**gRPC**) and asynchronous (**RabbitMQ / Azure Service Bus**) patterns:

* **`Identity Microservice`**: OAuth2, OpenID Connect, and JWT-based distributed security with role-based claims (Drivers, Warehouse Supervisors, Tenant Admins).
* **`Fleet Management`**: Transport asset tracking, driver states, and route planning.
* **`Cargo & Tracking`**: Ingestion of real-time telemetry from mobile devices with offline-first synchronization.
* **`Billing & Escrow`**: Automated distributed transactions managed through the **Saga Pattern** (Eventual Consistency and compensating rollbacks).

---

## 🛠️ Technology Stack
* **Backend Framework:** .NET 8 / 9 (ASP.NET Core WebAPIs, gRPC)
* **Data & ORM:** Entity Framework Core (SQL Server / NoSQL)
* **Mobile Client:** .NET MAUI (Cross-platform client for field transport operators)
* **Infrastructure & DevOps:** Docker, Containerization, Serilog (Centralized Logging), GitHub Projects & CLI (`gh`) for GitOps workflow.
* **Methodology:** Specification-Driven Development (SDD)

---

## 👥 Engineering Squad (ITM PDI74-3)
* **Strategy & Product:** John Sebastian Gomez
* **Technical Lead:** Santiago Martinez
* **Infrastructure & Security:** Jorge Armando Perez
* **QA & Resilience:** Emannuel Carvajal
* **ERP & Billing Processes:** Yordys Alfonso Leudo, Maria Alejandra Rua
* **Networking & Connectivity:** Juan de Dios Sanchez
* **Core Backend:** Juan Esteban Coneo, Yohan Esneider Granda, Maria Camila Sarmiento
* **Mobile Squad:** Sebastian Zuluaga, Luis Miguel Villada, Diego Alejandro Velasquez, Juan Pablo Henao

---

## 🚦 Getting Started
Please refer to our [Projects Board](https://github.com/) to check active Issues, User Stories, and Sprint milestones following our GitOps workflow.
