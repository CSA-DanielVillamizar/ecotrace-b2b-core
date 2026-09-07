# Plan de Evaluación — PD174-3, segundo periodo 2026

**Vigencia:** 8 de septiembre – 26 de noviembre de 2026 (24 sesiones de clase, martes y jueves).
**Estructura:** 5 eventos calificables, 20% cada uno (100% total) — 3 trabajos en equipo, 2 exámenes individuales.

Este plan continúa directamente el trabajo de las Clases 0-6: los 6 ADRs (`docs/adr/0001` a `0006`) documentan las **decisiones** de arquitectura de EcoTrace B2B. Los eventos evaluables de este periodo son la implementación real, en código, de esas decisiones — Specification-Driven Development llevado a su consecuencia lógica: la especificación no describe el sistema después de construido, lo construye.

---

## Resumen

| # | Milestone | Tipo | Peso | ADR base | Fecha |
|---|---|---|---|---|---|
| 1 | [Trabajo 1](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/milestone/1) | Trabajo en equipo | 20% | ADR 0001 | jue 17-sep-2026 |
| 2 | [Trabajo 2](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/milestone/2) | Trabajo en equipo | 20% | ADR 0002/0003 | jue 1-oct-2026 |
| 3 | [Examen 1](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/milestone/3) | Individual escrito | 20% | ADR 0001-0003 | mar 6-oct-2026 |
| 4 | [Trabajo 3](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/milestone/4) | Capstone en equipo | 20% | ADR 0004/0005/0006 | jue 12-nov-2026 |
| 5 | [Examen 2](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/milestone/5) | Individual escrito + oral | 20% | Todo (0001-0006) | mar 24 / jue 26-nov-2026 |

Issues de tracking: [#22](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/22), [#23](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/23), [#24](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/24), [#25](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/25), [#26](https://github.com/CSA-DanielVillamizar/ecotrace-b2b-core/issues/26).

---

## Modalidad de calificación

- **Trabajos (1, 2, 4):** nota base grupal por equipo (los 4 escuadrones ya conformados: Identity, Fleet Management, Cargo & Tracking, Billing & Escrow), ajustada por un factor individual de **0.8× a 1.1×** según una sustentación oral de 3-5 minutos por persona, donde cada estudiante explica y defiende la parte del código que le correspondió. Este ajuste evita que alguien se cuelgue del trabajo del equipo.
- **Exámenes (3, 5):** 100% individual, sin ajuste grupal.

## Por qué esta distribución

- Los 3 trabajos son la continuación natural del formato GitOps/SDD usado toda la primera mitad del semestre: cada uno cierra con un PR de código real, revisado por otro equipo, no solo con documentación.
- El Trabajo 3 agrupa Seguridad + Móvil + Despliegue en un solo capstone porque en la práctica son una sola cadena de confianza: el JWT que Identity emite es el mismo que la app MAUI guarda en SecureStorage y el mismo que viaja en el build que se publica.
- Los 2 exámenes quedan en los puntos de corte clásicos del semestre (parcial a mitad de periodo, final al cierre): el Examen 1 llega justo después de los dos primeros trabajos, evaluando si el equipo entendió el porqué de lo que ya construyó — no contenido nuevo.

## Calendario de clases restantes (referencia)

24 sesiones (martes y jueves) desde el 8 de septiembre hasta el 26 de noviembre de 2026 — última clase real del semestre, dado que el 29 de noviembre (fecha de cierre administrativo) cae domingo.
