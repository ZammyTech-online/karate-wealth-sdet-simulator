# ──────────────────────────────────────────────────────────────────────
# L38 · E01 · Validación de SLA con responseTime
#
# Comprueba que la variable simulatedTime sea menor al umbral estipulado de 500 ms usando assert.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# assert evalúa expresiones JavaScript booleanas nativas.
# ──────────────────────────────────────────────────────────────────────
@L38 @E01 @solucion
Feature: L38 E01 · Validación de SLA con responseTime

  Scenario: E01 · Validación de SLA
    * def simulatedTime = 180
    * assert simulatedTime < 500
