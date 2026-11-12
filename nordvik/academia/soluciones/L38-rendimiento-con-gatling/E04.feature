# ──────────────────────────────────────────────────────────────────────
# L38 · E04 · Tasa máxima de error tolerada
#
# Verifica que la tasa de errores de la simulación sea inferior al 1% con assert.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Cero fallos HTTP 500 en transacciones financieras de traspaso de fondos.
# ──────────────────────────────────────────────────────────────────────
@L38 @E04 @solucion
Feature: L38 E04 · Tasa máxima de error tolerada

  Scenario: E04 · Tolerancia a fallos
    * def errorRate = 0.005
    * assert errorRate < 0.01
