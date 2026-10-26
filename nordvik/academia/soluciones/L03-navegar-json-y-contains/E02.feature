# ──────────────────────────────────────────────────────────────────────
# L03 · E02 · El tercer elemento
#
# La tercera comisión (fees[2]) es la de salida. ¿Cómo se llama su tipo? Míralo en Swagger.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L03 @E02 @solucion
Feature: L03 E02 · El tercer elemento

  Scenario: La tercera comisión es la de salida
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    And match response.fees[2].type == 'EXIT'
