# ──────────────────────────────────────────────────────────────────────
# L04 · E04 · Del más caro al más barato
#
# Ordena por comisión de forma descendente. El primero debe ser el más caro (1.75 %).
#
# Rellena cada ___ y comprueba con:  academia corregir L04
# Pista: El orden se controla con el parámetro order. Ascendente es 'asc'.
# ──────────────────────────────────────────────────────────────────────
@L04 @E04
Feature: L04 E04 · Del más caro al más barato

  Scenario: El fondo más caro primero
    Given url catalogUrl
    And path '/api/v1/funds'
    And param sort = 'fee'
    And param order = ___
    When method get
    Then status 200
    And match response.items[0].management_fee == '1.75'
