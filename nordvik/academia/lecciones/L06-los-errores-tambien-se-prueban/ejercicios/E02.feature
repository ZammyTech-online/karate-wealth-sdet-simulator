# ──────────────────────────────────────────────────────────────────────
# L06 · E02 · Una divisa no soportada
#
# fx-rates no trabaja con yenes (JPY). Pide el tipo EUR/JPY y predice el error.
#
# Rellena cada ___ y comprueba con:  academia corregir L06
# Pista: La petición es inválida (no es que falte algo). El code dice 'divisa no soportada' en inglés.
# ──────────────────────────────────────────────────────────────────────
@L06 @E02
Feature: L06 E02 · Una divisa no soportada

  Scenario: El yen no está soportado
    Given url fxUrl
    And path '/api/v1/rates/EUR/JPY'
    When method get
    Then status ___
    And match response.code == ___
