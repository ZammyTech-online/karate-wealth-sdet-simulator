# ──────────────────────────────────────────────────────────────────────
# L27 · E03 · Adelanta el reloj
#
# La gestora tarda 2 días. Congela el reloj 2 días después (mismo día a las 10:30) y completa el estado de la saga.
#
# Rellena cada ___ y comprueba con:  academia corregir L27
# Pista: El ayudante congeló el martes 2026-10-20 a las 10:00. Dos días después es jueves 22.
# ──────────────────────────────────────────────────────────────────────
@L27 @E03
Feature: L27 E03 · Adelanta el reloj

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Pasan dos días
    * def t = call read('_traspaso-listo.feature') { gestora: 'NORRBO', valor: '95000.00', dias: 2, desviacion: 0 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'NORRBO', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    * def id = response.id
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: ___ }
    When method post
    Then status 200
    # Dos días después el token de antes ha caducado (también en tiempo virtual): login otra vez
    * def erik = call read('_login.feature') usuarios.erik
    Given url transfersUrl
    And path '/api/v1/transfers', id
    And headers erik.auth
    When method get
    Then status 200
    And match response.status == ___
    And match response.valuedAmount == '95000.00'
