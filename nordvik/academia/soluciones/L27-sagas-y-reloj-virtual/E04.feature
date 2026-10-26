# ──────────────────────────────────────────────────────────────────────
# L27 · E04 · Aprueba Operaciones
#
# Con demora 0 la valoración llega al momento. Confirma y aprueba. Completa quién aprueba y el estado final.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L27 @E04 @solucion
Feature: L27 E04 · Aprueba Operaciones

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Aprobado
    * def t = call read('_traspaso-listo.feature') { gestora: 'SUNDPENSION', valor: '60000.00', dias: 0, desviacion: 0 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'SUNDPENSION', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    And match response.status == 'VALUED'
    * def id = response.id
    Given path '/api/v1/transfers', id, 'confirm'
    And headers t.erik.auth
    When method post
    Then status 200
    Given path '/api/v1/transfers', id, 'approve'
    And headers t.olof.auth
    When method post
    Then status 200
    And match response.status == 'COMPLETED'
    And match response.contributionId == '#regex ctb_.+'
