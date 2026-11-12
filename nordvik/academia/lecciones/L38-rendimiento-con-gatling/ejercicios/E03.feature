# ──────────────────────────────────────────────────────────────────────
# L38 · E03 · Umbral de percentil 95 (p95)
#
# Define el criterio de aceptación actuarial: p95 debe ser inferior o igual a 300 milisegundos con assert.
#
# Rellena cada ___ y comprueba con:  academia corregir L38
# Pista: assert p95Threshold <= 300
# ──────────────────────────────────────────────────────────────────────
@L38 @E03
Feature: L38 E03 · Umbral de percentil 95 (p95)

  Scenario: E03 · Umbral de percentil 95
    * def p95Threshold = ___
    * ___
