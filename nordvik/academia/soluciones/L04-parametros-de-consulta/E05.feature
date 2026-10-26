# ──────────────────────────────────────────────────────────────────────
# L04 · E05 · Un parámetro fuera de rango
#
# Pedimos páginas de 500, pero el máximo es 100. Predice el código y el campo code del error.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L04 @E05 @solucion
Feature: L04 E05 · Un parámetro fuera de rango

  Scenario: page_size demasiado grande
    Given url catalogUrl
    And path '/api/v1/funds'
    And param page_size = 500
    When method get
    Then status 400
    And match response.code == 'INVALID_QUERY'
