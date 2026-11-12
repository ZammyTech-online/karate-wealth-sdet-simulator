# ──────────────────────────────────────────────────────────────────────
# L39 · E02 · Ruta del reporte interactivo de Karate
#
# Especifica la ruta estándar donde Karate genera el resumen HTML de resultados.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Este archivo HTML contiene el desglose visual de cada petición y aserción.
# ──────────────────────────────────────────────────────────────────────
@L39 @E02 @solucion
Feature: L39 E02 · Ruta del reporte interactivo de Karate

  Scenario: E02 · Ruta del reporte HTML
    * def reportPath = 'target/karate-reports/karate-summary.html'
    * match reportPath contains 'karate-summary.html'
