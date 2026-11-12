# Ayudante: un cliente nuevo de asesor.erik con un plan de pensión PRIVATE. Devuelve customerId, planId y los logins de erik y olof.
# Uso:  * def p = call read('_plan.feature')
@ignore
Feature: Ayudante · cliente con plan de pensión

  Scenario: Plan
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
