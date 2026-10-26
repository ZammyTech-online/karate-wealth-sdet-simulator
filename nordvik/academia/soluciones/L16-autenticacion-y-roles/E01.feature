# ──────────────────────────────────────────────────────────────────────
# L16 · E01 · Tu primer token
#
# Pide un token como la clienta Ana (usuario cliente.ana, clave Ana2026!).
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L16 @E01 @solucion
Feature: L16 E01 · Tu primer token

  Scenario: Login de Ana
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = 'cliente.ana'
    And form field password = 'Ana2026!'
    When method post
    Then status 200
    And match response.access_token == '#string'
