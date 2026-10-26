# ──────────────────────────────────────────────────────────────────────
# L33 · E04 · El efecto en pii-vault (eventual)
#
# El token del cliente acaba ERASED por un evento. Completa la condición de espera.
#
# Rellena cada ___ y comprueba con:  academia corregir L33
# Pista: Hay que esperar a que el estado del token cambie: retry until con la condición.
# ──────────────────────────────────────────────────────────────────────
@L33 @E04
Feature: L33 E04 · El efecto en pii-vault (eventual)

  Scenario: Token borrado
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'EMAIL', value: '#(c.customerId.substring(0, 8) + "@example.com")', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    * def token = response.token
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    * configure retry = { count: 30, interval: 300 }
    Given url piiVaultUrl
    And path '/api/v1/tokens', token
    And headers karin.auth
    And retry until ___
    When method get
    Then status 200
