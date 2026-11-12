# ──────────────────────────────────────────────────────────────────────
# L38 · E05 · Reutilización de escenarios funcionales
#
# Indica el path del feature funcional que se reutiliza como prueba de carga.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# El mayor superpoder de Karate-Gatling es no tener que mantener dos suites distintas.
# ──────────────────────────────────────────────────────────────────────
@L38 @E05 @solucion
Feature: L38 E05 · Reutilización de escenarios funcionales

  Scenario: E05 · Ruta de feature reutilizado
    * def featurePath = 'classpath:lecciones/L01-primera-peticion/ejemplo.feature'
    * match featurePath == '#present'
