# ──────────────────────────────────────────────────────────────────────
# L04 · E01 · Filtrar por categoría
#
# Pide solo los fondos de renta fija. Hay 3.
#
# Rellena cada ___ y comprueba con:  academia corregir L04
# Pista: Las categorías van en inglés y en mayúsculas, con guion bajo si tienen dos palabras.
# ──────────────────────────────────────────────────────────────────────
@L04 @E01
Feature: L04 E01 · Filtrar por categoría

  Scenario: Los fondos de renta fija
    Given url catalogUrl
    And path '/api/v1/funds'
    And param category = ___
    When method get
    Then status 200
    And match response.total == 3
