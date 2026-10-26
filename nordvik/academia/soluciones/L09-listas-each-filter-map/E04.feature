# ──────────────────────────────────────────────────────────────────────
# L09 · E04 · Un campo de todos con JsonPath
#
# Saca la lista de ISIN de todos los fondos sin escribir una función.
# Ojo: para guardar una expresión JsonPath en una variable, def necesita la palabra get.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L09 @E04 @solucion
Feature: L09 E04 · Un campo de todos con JsonPath

  Scenario: Todos los ISIN
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def isins = get response.items[*].isin
    * match isins == '#[14]'
    * match each isins == '#regex [A-Z]{2}[0-9A-Z]{9}[0-9]'
