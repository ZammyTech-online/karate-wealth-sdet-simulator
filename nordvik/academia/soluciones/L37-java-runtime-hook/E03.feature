# ──────────────────────────────────────────────────────────────────────
# L37 · E03 · Lectura de metadatos del escenario
#
# Accede a la variable karate.scenario para inspeccionar el nombre del escenario en ejecución.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Permite generar auditorías y logs detallados con el nombre exacto de la prueba.
# ──────────────────────────────────────────────────────────────────────
@L37 @E03 @solucion
Feature: L37 E03 · Lectura de metadatos del escenario

  Scenario: E03 · Metadatos de escenario
    * def name = karate.scenario.name
    * match name == '#present'
