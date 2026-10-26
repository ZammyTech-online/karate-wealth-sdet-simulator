# ──────────────────────────────────────────────────────────────────────
# L27 · E02 · No te saltes pasos
#
# Confirma antes de que llegue la valoración. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L27 @E02 @solucion
Feature: L27 E02 · No te saltes pasos

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Confirmar demasiado pronto
    * def t = call read('_traspaso-listo.feature') { gestora: 'FJORDLIV', valor: '80000.00', dias: 2, desviacion: 0 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'FJORDLIV', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    Given path '/api/v1/transfers', response.id, 'confirm'
    And headers t.erik.auth
    When method post
    Then status 409
    And match response.code == 'INVALID_STATE_TRANSITION'
