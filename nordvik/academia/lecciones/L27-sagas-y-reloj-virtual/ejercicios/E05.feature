# ──────────────────────────────────────────────────────────────────────
# L27 · E05 · Fuera de tolerancia
#
# La gestora manda un 5 % más de lo valorado. Completa el estado al que va la saga y la desviación.
#
# Rellena cada ___ y comprueba con:  academia corregir L27
# Pista: Más del 2 %: alguien tiene que revisarlo a mano. La desviación sale con 2 decimales, como texto.
# ──────────────────────────────────────────────────────────────────────
@L27 @E05
Feature: L27 E05 · Fuera de tolerancia

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Revisión manual
    * def t = call read('_traspaso-listo.feature') { gestora: 'SUNDPENSION', valor: '50000.00', dias: 0, desviacion: 5 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'SUNDPENSION', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v1/transfers', id, 'confirm'
    And headers t.erik.auth
    When method post
    Given path '/api/v1/transfers', id, 'approve'
    And headers t.olof.auth
    When method post
    Then status 200
    And match response.status == ___
    And match response.deviationPct == ___
    And match response.receivedAmount == '52500.00'
