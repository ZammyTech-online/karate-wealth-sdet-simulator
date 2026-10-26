# ──────────────────────────────────────────────────────────────────────
# L11 · E04 · ¿Quién me ha contestado?
#
# Comprueba con la cabecera X-Service qué servicio respondió al tipo de cambio.
#
# Rellena cada ___ y comprueba con:  academia corregir L11
# Pista: Es el nombre del microservicio de tipos de cambio.
# ──────────────────────────────────────────────────────────────────────
@L11 @E04
Feature: L11 E04 · ¿Quién me ha contestado?

  Scenario: Responde fx-rates
    Given url fxUrl
    And path '/api/v1/rates/EUR/SEK'
    When method get
    Then status 200
    And match responseHeaders['X-Service'][0] == ___
