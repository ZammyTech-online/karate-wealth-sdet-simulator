# Ejemplo resuelto de L27. Ejecútalo con:  academia ejemplo L27
@ejemplo
Feature: L27 · Un traspaso completo

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: De la solicitud al dinero
    * def t = call read('ejercicios/_traspaso-listo.feature') { gestora: 'FJORDLIV', valor: '120000.00', dias: 2, desviacion: 0.5 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'FJORDLIV', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    And match response.status == 'VALUATION_PENDING'
    * def id = response.id
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-22T10:30:00' }
    When method post
    Then status 200
    # Los tokens también caducan en tiempo virtual: tras dos días, login otra vez
    * def erik = call read('ejercicios/_login.feature') usuarios.erik
    * def olof = call read('ejercicios/_login.feature') usuarios.olof
    Given url transfersUrl
    And path '/api/v1/transfers', id
    And headers erik.auth
    When method get
    Then match response.status == 'VALUED'
    Given path '/api/v1/transfers', id, 'confirm'
    And headers erik.auth
    When method post
    Then match response.status == 'AWAITING_APPROVAL'
    Given path '/api/v1/transfers', id, 'approve'
    And headers olof.auth
    When method post
    Then status 200
    And match response contains { status: 'COMPLETED', receivedAmount: '120600.00', deviationPct: '0.50' }
