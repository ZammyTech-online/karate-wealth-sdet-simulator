# ──────────────────────────────────────────────────────────────────────
# L06 · E02 · Una divisa no soportada
#
# fx-rates no trabaja con yenes (JPY). Pide el tipo EUR/JPY y predice el error.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L06 @E02 @solucion
Feature: L06 E02 · Una divisa no soportada

  Scenario: El yen no está soportado
    Given url fxUrl
    And path '/api/v1/rates/EUR/JPY'
    When method get
    Then status 400
    And match response.code == 'UNSUPPORTED_CURRENCY'
