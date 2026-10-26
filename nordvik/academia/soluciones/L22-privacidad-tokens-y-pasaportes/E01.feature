# ──────────────────────────────────────────────────────────────────────
# L22 · E01 · Cambia un dato por un token
#
# Tokeniza el número nacional de un cliente nuevo. Completa el estado y el prefijo del token.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L22 @E01 @solucion
Feature: L22 E01 · Cambia un dato por un token

  Scenario: Tokenizar
    * def c = call read('_cliente.feature')
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    And match response.token == '#regex tok_nid_.+'
