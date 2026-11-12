# ──────────────────────────────────────────────────────────────────────
# L39 · E02 · Ruta del reporte interactivo de Karate
#
# Especifica la ruta estándar donde Karate genera el resumen HTML de resultados.
#
# Rellena cada ___ y comprueba con:  academia corregir L39
# Pista: target/karate-reports/karate-summary.html
# ──────────────────────────────────────────────────────────────────────
@L39 @E02
Feature: L39 E02 · Ruta del reporte interactivo de Karate

  Scenario: E02 · Ruta del reporte HTML
    * def reportPath = ___
    * match reportPath contains ___
