# ──────────────────────────────────────────────────────────────────────
# L02 · E05 · Un fondo cerrado
#
# El fondo SE0000000007 (Nordvik Frontier Markets) ya no admite inversiones. ¿Qué dice su campo status?
#
# Rellena cada ___ y comprueba con:  academia corregir L02
# Pista: Si un fondo abierto es 'OPEN', uno cerrado es su contrario en inglés y en mayúsculas.
# ──────────────────────────────────────────────────────────────────────
@L02 @E05
Feature: L02 E05 · Un fondo cerrado

  Scenario: Frontier Markets está cerrado
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000007'
    When method get
    Then status 200
    And match response.status == ___
