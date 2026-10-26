# ──────────────────────────────────────────────────────────────────────
# L03 · E01 · El primer elemento de una lista
#
# Comprueba que la primera comisión del fondo SE0000000001 es la de gestión (MANAGEMENT).
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L03 @E01 @solucion
Feature: L03 E01 · El primer elemento de una lista

  Scenario: La primera comisión es la de gestión
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    And match response.fees[0].type == 'MANAGEMENT'
