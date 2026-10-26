# Ayudante: congela el reloj en el martes de pruebas, crea un cliente nuevo con un plan de pensión PRIVATE
# y una póliza en la gestora simulada (E10). Devuelve customerId, planId, poliza y los logins.
# Uso:  * def t = call read('_traspaso-listo.feature') { gestora: 'FJORDLIV', valor: '120000.00', dias: 2, desviacion: 0.5 }
@ignore
Feature: Ayudante · cliente con plan y póliza externa

  Scenario: Preparar un traspaso
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-20T10:00:00' }
    When method post
    Then status 200
    * def c = call read('this:_cliente.feature')
    * def erik = c.erik
    * def olof = call read('this:_login.feature') usuarios.olof
    * def customerId = c.customerId
    Given url pensionPlansUrl
    And path '/api/v1/pension-plans'
    And headers erik.auth
    And request { customerId: '#(customerId)', type: 'PRIVATE', allocation: [{ isin: 'SE0000000203', percent: 100 }] }
    When method post
    Then status 201
    * def planId = response.id
    * def poliza = 'P' + (100000 + Math.floor(Math.random() * 899999))
    Given url providerSimUrl
    And path '/__admin/policies'
    And request { providerCode: '#(gestora)', policyNumber: '#(poliza)', value: '#(valor)', delayDays: '#(dias)', driftPct: '#(desviacion)' }
    When method post
    Then status 200
