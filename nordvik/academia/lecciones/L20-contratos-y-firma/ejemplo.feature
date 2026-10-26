# Ejemplo resuelto de L20. Ejecútalo con:  academia ejemplo L20
@ejemplo
Feature: L20 · Del borrador al contrato activo

  Background:
    * def erik = callonce read('ejercicios/_login.feature') usuarios.erik
    * def ana = callonce read('ejercicios/_login.feature') usuarios.ana
    * configure retry = { count: 30, interval: 1000 }
    * url contractsUrl

  Scenario: Ana firma su contrato marco
    Given path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    And match response.status == 'DRAFT'
    * def contrato = response

    Given path '/api/v2/contracts', contrato.id, 'send'
    And headers erik.auth
    And request {}
    When method post
    Then status 200
    And match response.status == 'PENDING_SIGNATURE'

    Given path '/api/v2/contracts', contrato.id, 'signing-sessions'
    And headers ana.auth
    And request { party: 'CUSTOMER' }
    When method post
    Then status 201
    And match response.signedHash == contrato.contentHash
    * def sesion = response

    Given path '/api/v2/contracts', contrato.id, 'signing-sessions', sesion.id
    And headers ana.auth
    And retry until response.status != 'PENDING'
    When method get
    Then status 200
    And match response.status == 'COMPLETED'

    Given path '/api/v2/contracts', contrato.id
    And headers ana.auth
    And retry until response.status == 'ACTIVE'
    When method get
    Then status 200
