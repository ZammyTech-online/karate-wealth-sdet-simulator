# ──────────────────────────────────────────────────────────────────────
# L03 · E05 · Un valor dentro de todos los elementos
#
# El fondo monetario tiene el ISIN SE0000000301. Comprueba que está en la lista de ISIN del catálogo.
#
# Rellena cada ___ y comprueba con:  academia corregir L03
# Pista: response.items[*].isin es la lista con el ISIN de cada fondo. Busca un texto en ella.
# ──────────────────────────────────────────────────────────────────────
@L03 @E05
Feature: L03 E05 · Un valor dentro de todos los elementos

  Scenario: El fondo monetario está en el catálogo
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    And match response.items[*].isin contains ___
