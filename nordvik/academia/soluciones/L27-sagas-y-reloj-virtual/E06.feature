# ──────────────────────────────────────────────────────────────────────
# L27 · E06 · El historial cuenta la saga
#
# Tras aceptar el importe en revisión manual, valida el historial completo. Completa el camino de estados.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L27 @E06 @solucion
Feature: L27 E06 · El historial cuenta la saga

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Historial
    * def t = call read('_traspaso-listo.feature') { gestora: 'NORRBO', valor: '40000.00', dias: 0, desviacion: -3 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'NORRBO', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v1/transfers', id, 'confirm'
    And headers t.erik.auth
    When method post
    Given path '/api/v1/transfers', id, 'approve'
    And headers t.olof.auth
    When method post
    Given path '/api/v1/transfers', id, 'accept-amount'
    And headers t.olof.auth
    When method post
    Then status 200
    And match response.history[*].to == ['VALUATION_PENDING', 'VALUED', 'AWAITING_APPROVAL', 'MANUAL_REVIEW', 'COMPLETED']
