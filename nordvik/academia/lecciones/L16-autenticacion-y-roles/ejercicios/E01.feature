# ──────────────────────────────────────────────────────────────────────
# L16 · E01 · Tu primer token
#
# Pide un token como la clienta Ana (usuario cliente.ana, clave Ana2026!).
#
# Rellena cada ___ y comprueba con:  academia corregir L16
# Pista: El usuario va en el campo username del formulario, entre comillas.
# ──────────────────────────────────────────────────────────────────────
@L16 @E01
Feature: L16 E01 · Tu primer token

  Scenario: Login de Ana
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = ___
    And form field password = 'Ana2026!'
    When method post
    Then status 200
    And match response.access_token == '#string'
