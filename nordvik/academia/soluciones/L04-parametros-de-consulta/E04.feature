# ──────────────────────────────────────────────────────────────────────
# L04 · E04 · Del más caro al más barato
#
# Ordena por comisión de forma descendente. El primero debe ser el más caro (1.75 %).
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L04 @E04 @solucion
Feature: L04 E04 · Del más caro al más barato

  Scenario: El fondo más caro primero
    Given url catalogUrl
    And path '/api/v1/funds'
    And param sort = 'fee'
    And param order = 'desc'
    When method get
    Then status 200
    And match response.items[0].management_fee == '1.75'
