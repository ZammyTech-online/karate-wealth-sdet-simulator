# ──────────────────────────────────────────────────────────────────────
# L11 · E04 · ¿Quién me ha contestado?
#
# Comprueba con la cabecera X-Service qué servicio respondió al tipo de cambio.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L11 @E04 @solucion
Feature: L11 E04 · ¿Quién me ha contestado?

  Scenario: Responde fx-rates
    Given url fxUrl
    And path '/api/v1/rates/EUR/SEK'
    When method get
    Then status 200
    And match responseHeaders['X-Service'][0] == 'fx-rates'
