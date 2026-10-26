# ──────────────────────────────────────────────────────────────────────
# L31 · E01 · Un esquema en snake_case
#
# Completa el nombre del campo de moneda base tal como lo escribe product-catalog y el marcador del número de riesgo.
#
# Rellena cada ___ y comprueba con:  academia corregir L31
# Pista: Este servicio usa snake_case: palabras en minúscula unidas por _. El riesgo es un número.
# ──────────────────────────────────────────────────────────────────────
@L31 @E01
Feature: L31 E01 · Un esquema en snake_case

  Scenario: Esquema del fondo
    * def fondo = { isin: '#string', name: '#string', ___: '#string', sri: ___ }
    Given url catalogUrl
    And path '/api/v1/funds/SE0000000002'
    When method get
    Then status 200
    And match response contains fondo
