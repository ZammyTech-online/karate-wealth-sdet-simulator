# ──────────────────────────────────────────────────────────────────────
# L06 · E06 · Apagar el modo didáctico
#
# Envía la cabecera X-Learning-Mode con el valor false. El error ya no debe traer hint.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L06 @E06 @solucion
Feature: L06 E06 · Apagar el modo didáctico

  Scenario: Sin pistas en modo profesional
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    And header X-Learning-Mode = 'false'
    When method get
    Then status 404
    And match response.code == 'FUND_NOT_FOUND'
    And assert response.hint == null
