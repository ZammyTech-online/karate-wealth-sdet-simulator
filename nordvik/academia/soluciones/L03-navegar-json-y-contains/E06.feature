# ──────────────────────────────────────────────────────────────────────
# L03 · E06 · Exactamente estos, en cualquier orden
#
# Comprueba que los tipos de comisión son exactamente EXIT, MANAGEMENT y ENTRY, sin importar el orden
# y sin que falte ni sobre ninguno.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L03 @E06 @solucion
Feature: L03 E06 · Exactamente estos, en cualquier orden

  Scenario: Los tres tipos de comisión
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000201'
    When method get
    Then status 200
    And match response.fees[*].type contains only ['EXIT', 'MANAGEMENT', 'ENTRY']
