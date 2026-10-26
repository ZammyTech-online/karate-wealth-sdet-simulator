# ──────────────────────────────────────────────────────────────────────
# L09 · E04 · Un campo de todos con JsonPath
#
# Saca la lista de ISIN de todos los fondos sin escribir una función.
# Ojo: para guardar una expresión JsonPath en una variable, def necesita la palabra get.
#
# Rellena cada ___ y comprueba con:  academia corregir L09
# Pista: Con [*] sacas un campo de todos los elementos de una lista; delante va la palabra get.
# ──────────────────────────────────────────────────────────────────────
@L09 @E04
Feature: L09 E04 · Un campo de todos con JsonPath

  Scenario: Todos los ISIN
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def isins = ___
    * match isins == '#[14]'
    * match each isins == '#regex [A-Z]{2}[0-9A-Z]{9}[0-9]'
