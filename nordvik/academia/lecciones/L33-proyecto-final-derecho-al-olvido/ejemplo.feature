# Ejemplo resuelto de L33. Ejecútalo con:  academia ejemplo L33
@ejemplo
Feature: L33 · Borrado completo

  Scenario: Tokenizar, borrar y comprobar el efecto
    * def c = call read('ejercicios/_cliente.feature')
    * def karin = call read('ejercicios/_login.feature') usuarios.karin
    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    * def iban = response.electronic
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'IBAN', value: '#(iban)', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    * def token = response.token
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    And match response.status == 'COMPLETED'
    * configure retry = { count: 30, interval: 300 }
    Given url piiVaultUrl
    And path '/api/v1/tokens', token
    And headers karin.auth
    And retry until response.status == 'ERASED'
    When method get
    Then status 200
