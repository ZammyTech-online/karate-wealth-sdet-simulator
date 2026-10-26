# ──────────────────────────────────────────────────────────────────────
# L06 · E06 · Apagar el modo didáctico
#
# Envía la cabecera X-Learning-Mode con el valor false. El error ya no debe traer hint.
#
# Rellena cada ___ y comprueba con:  academia corregir L06
# Pista: Las cabeceras se envían con header Nombre = valor, y el valor es un texto.
# ──────────────────────────────────────────────────────────────────────
@L06 @E06
Feature: L06 E06 · Apagar el modo didáctico

  Scenario: Sin pistas en modo profesional
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    And header X-Learning-Mode = ___
    When method get
    Then status 404
    And match response.code == 'FUND_NOT_FOUND'
    And assert response.hint == null
