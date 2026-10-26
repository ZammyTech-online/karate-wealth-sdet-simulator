# ──────────────────────────────────────────────────────────────────────
# L03 · E05 · Un valor dentro de todos los elementos
#
# El fondo monetario tiene el ISIN SE0000000301. Comprueba que está en la lista de ISIN del catálogo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L03 @E05 @solucion
Feature: L03 E05 · Un valor dentro de todos los elementos

  Scenario: El fondo monetario está en el catálogo
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    And match response.items[*].isin contains 'SE0000000301'
