# ──────────────────────────────────────────────────────────────────────
# L22 · E03 · El mismo dato, el mismo token
#
# Tokenizar dos veces lo mismo no crea nada nuevo. Completa el estado de la segunda vez y la comparación.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L22 @E03 @solucion
Feature: L22 E03 · El mismo dato, el mismo token

  Scenario: Determinista
    * def c = call read('_cliente.feature')
    * def cuerpo = { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request cuerpo
    When method post
    Then status 201
    * def primero = response.token
    Given path '/api/v1/tokens'
    And headers c.erik.auth
    And request cuerpo
    When method post
    Then status 200
    And match response.token == primero
