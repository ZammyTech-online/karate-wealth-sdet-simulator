# ──────────────────────────────────────────────────────────────────────
# L01 · E03 · Una ruta con una variable
#
# El ISIN ya está guardado en la variable isin. Úsala como segundo trozo de la ruta.
#
# Rellena cada ___ y comprueba con:  academia corregir L01
# Pista: Los trozos del path se separan con comas, y una variable va sin comillas.
# ──────────────────────────────────────────────────────────────────────
@L01 @E03
Feature: L01 E03 · Una ruta con una variable

  Scenario: Detalle de un fondo guardado en una variable
    * def isin = 'SE0000000002'
    Given url catalogUrl
    And path '/api/v1/funds', ___
    When method get
    Then status 200
    And match response.name == 'Nordvik Sverige Index'
