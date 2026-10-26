# ──────────────────────────────────────────────────────────────────────
# L22 · E03 · El mismo dato, el mismo token
#
# Tokenizar dos veces lo mismo no crea nada nuevo. Completa el estado de la segunda vez y la comparación.
#
# Rellena cada ___ y comprueba con:  academia corregir L22
# Pista: No se crea un recurso nuevo: es un 'aquí tienes el que ya existía'.
# ──────────────────────────────────────────────────────────────────────
@L22 @E03
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
    Then status ___
    And match response.token == ___
