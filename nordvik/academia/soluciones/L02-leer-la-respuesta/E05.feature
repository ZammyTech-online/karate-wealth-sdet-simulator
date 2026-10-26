# ──────────────────────────────────────────────────────────────────────
# L02 · E05 · Un fondo cerrado
#
# El fondo SE0000000007 (Nordvik Frontier Markets) ya no admite inversiones. ¿Qué dice su campo status?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L02 @E05 @solucion
Feature: L02 E05 · Un fondo cerrado

  Scenario: Frontier Markets está cerrado
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000007'
    When method get
    Then status 200
    And match response.status == 'CLOSED'
