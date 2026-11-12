# ──────────────────────────────────────────────────────────────────────
# L38 · E03 · Umbral de percentil 95 (p95)
#
# Define el criterio de aceptación actuarial: p95 debe ser inferior o igual a 300 milisegundos con assert.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# El p95 asegura que el 95% de las transacciones son fluidas para los clientes.
# ──────────────────────────────────────────────────────────────────────
@L38 @E03 @solucion
Feature: L38 E03 · Umbral de percentil 95 (p95)

  Scenario: E03 · Umbral de percentil 95
    * def p95Threshold = 300
    * assert p95Threshold <= 300
