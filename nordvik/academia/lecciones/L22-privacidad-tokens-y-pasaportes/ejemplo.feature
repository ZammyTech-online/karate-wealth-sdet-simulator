# Ejemplo resuelto de L22. Ejecútalo con:  academia ejemplo L22
@ejemplo
Feature: L22 · Tokens y pasaportes

  Background:
    * def karin = callonce read('ejercicios/_login.feature') usuarios.karin
    * def olof = callonce read('ejercicios/_login.feature') usuarios.olof

  Scenario: Tokenizar un IBAN y verlo en claro con una finalidad válida
    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    Then status 201
    * def iban = response.electronic
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'IBAN', value: '#(iban)', customerId: '#(ids.ana)' }
    When method post
    Then status 201
    * def token = response.token
    * match karate.toString(response) !contains iban
    Given path '/api/v1/tokens', token, 'detokenize'
    And headers karin.auth
    And request { purpose: 'KYC_REVIEW' }
    When method post
    Then status 200
    And match response.value == iban

  Scenario: Un pasaporte de un solo uso
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'EMAIL', value: '#("ej" + java.util.UUID.randomUUID().toString().substring(0, 6) + "@example.com")', customerId: '#(ids.ana)' }
    When method post
    Then status 201
    * def token = response.token
    Given url passportsUrl
    And path '/api/v1/passports'
    And headers olof.auth
    And request { token: '#(token)', purpose: 'PAYMENT_EXECUTION', audience: 'payments-gateway' }
    When method post
    Then status 201
    And match response.readsLeft == 1
    * def id = response.id
    Given path '/api/v1/passports', id, 'read'
    And headers olof.auth
    And header X-Caller-Service = 'payments-gateway'
    When method post
    Then status 200
    And match response.readsLeft == 0
