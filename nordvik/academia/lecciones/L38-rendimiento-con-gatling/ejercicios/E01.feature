# ──────────────────────────────────────────────────────────────────────
# L38 · E01 · Validación de SLA con responseTime
#
# Comprueba que la variable simulatedTime sea menor al umbral estipulado de 500 ms usando assert.
#
# Rellena cada ___ y comprueba con:  academia corregir L38
# Pista: assert simulatedTime < 500
# ──────────────────────────────────────────────────────────────────────
@L38 @E01
Feature: L38 E01 · Validación de SLA con responseTime

  Scenario: E01 · Validación de SLA
    * def simulatedTime = 180
    * ___
