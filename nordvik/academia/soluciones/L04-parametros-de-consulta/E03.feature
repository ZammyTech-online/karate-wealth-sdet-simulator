# ──────────────────────────────────────────────────────────────────────
# L04 · E03 · Varios parámetros de una vez
#
# Con un solo params, pide los fondos en dólares (USD) con riesgo mínimo 7. Hay 2.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L04 @E03 @solucion
Feature: L04 E03 · Varios parámetros de una vez

  Scenario: Fondos en dólares de máximo riesgo
    Given url catalogUrl
    And path '/api/v1/funds'
    And params { currency: 'USD', min_sri: 7 }
    When method get
    Then status 200
    And match response.total == 2
