# ──────────────────────────────────────────────────────────────────────
# L22 · E01 · Cambia un dato por un token
#
# Tokeniza el número nacional de un cliente nuevo. Completa el estado y el prefijo del token.
#
# Rellena cada ___ y comprueba con:  academia corregir L22
# Pista: Es un recurso nuevo. Los tokens de número nacional empiezan por tok_nid_.
# ──────────────────────────────────────────────────────────────────────
@L22 @E01
Feature: L22 E01 · Cambia un dato por un token

  Scenario: Tokenizar
    * def c = call read('_cliente.feature')
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    When method post
    Then status ___
    And match response.token == ___
