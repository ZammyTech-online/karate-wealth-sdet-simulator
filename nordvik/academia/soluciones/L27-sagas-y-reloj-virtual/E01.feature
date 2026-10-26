# ──────────────────────────────────────────────────────────────────────
# L27 · E01 · Empieza esperando
#
# Solicita el traspaso. Completa el estado HTTP y el estado de la saga.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L27 @E01 @solucion
Feature: L27 E01 · Empieza esperando

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Solicitud
    * def t = call read('_traspaso-listo.feature') { gestora: 'FJORDLIV', valor: '80000.00', dias: 2, desviacion: 0 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'FJORDLIV', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    And match response.status == 'VALUATION_PENDING'
