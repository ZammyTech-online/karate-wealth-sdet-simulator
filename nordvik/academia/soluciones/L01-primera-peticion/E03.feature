# ──────────────────────────────────────────────────────────────────────
# L01 · E03 · Una ruta con una variable
#
# El ISIN ya está guardado en la variable isin. Úsala como segundo trozo de la ruta.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L01 @E03 @solucion
Feature: L01 E03 · Una ruta con una variable

  Scenario: Detalle de un fondo guardado en una variable
    * def isin = 'SE0000000002'
    Given url catalogUrl
    And path '/api/v1/funds', isin
    When method get
    Then status 200
    And match response.name == 'Nordvik Sverige Index'
