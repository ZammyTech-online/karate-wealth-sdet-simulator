# ──────────────────────────────────────────────────────────────────────
# L04 · E05 · Un parámetro fuera de rango
#
# Pedimos páginas de 500, pero el máximo es 100. Predice el código y el campo code del error.
#
# Rellena cada ___ y comprueba con:  academia corregir L04
# Pista: No es que no exista: la petición es inválida. El code lo verás en Swagger o ejecutando con print response.
# ──────────────────────────────────────────────────────────────────────
@L04 @E05
Feature: L04 E05 · Un parámetro fuera de rango

  Scenario: page_size demasiado grande
    Given url catalogUrl
    And path '/api/v1/funds'
    And param page_size = 500
    When method get
    Then status ___
    And match response.code == ___
