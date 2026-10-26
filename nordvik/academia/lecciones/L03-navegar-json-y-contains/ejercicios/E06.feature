# ──────────────────────────────────────────────────────────────────────
# L03 · E06 · Exactamente estos, en cualquier orden
#
# Comprueba que los tipos de comisión son exactamente EXIT, MANAGEMENT y ENTRY, sin importar el orden
# y sin que falte ni sobre ninguno.
#
# Rellena cada ___ y comprueba con:  academia corregir L03
# Pista: contains permite que sobren; == exige el orden. Hay una variante con dos palabras.
# ──────────────────────────────────────────────────────────────────────
@L03 @E06
Feature: L03 E06 · Exactamente estos, en cualquier orden

  Scenario: Los tres tipos de comisión
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000201'
    When method get
    Then status 200
    And match response.fees[*].type ___ ['EXIT', 'MANAGEMENT', 'ENTRY']
