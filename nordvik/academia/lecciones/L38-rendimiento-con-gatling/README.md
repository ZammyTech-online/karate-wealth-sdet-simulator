# L38 · Pruebas de Carga y Estrés con Karate Gatling

Un SDET moderno no reescribe los tests de API en otra herramienta para medir rendimiento: **reutiliza los mismos archivos .feature de Karate como escenarios de simulación de carga en Gatling**.

## La norma
- Clase Scala o Java que extiende `Simulation`.
- `karateFeature("classpath:flujo-pension.feature")`
- Inyección de carga: `rampUsers(50).during(Duration.ofSeconds(30))`.
- Métricas: Percentil 95 (p95 < 200ms), tasa de error (< 0.1%).
