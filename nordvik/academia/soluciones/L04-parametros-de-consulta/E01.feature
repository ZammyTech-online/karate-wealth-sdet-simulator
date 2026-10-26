# ──────────────────────────────────────────────────────────────────────
# L04 · E01 · Filtrar por categoría
#
# Pide solo los fondos de renta fija. Hay 3.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L04 @E01 @solucion
Feature: L04 E01 · Filtrar por categoría

  Scenario: Los fondos de renta fija
    Given url catalogUrl
    And path '/api/v1/funds'
    And param category = 'FIXED_INCOME'
    When method get
    Then status 200
    And match response.total == 3
