# ──────────────────────────────────────────────────────────────────────
# L18 · E05 · Un estado final malo también es final
#
# Una condición robusta acepta los dos finales posibles. Completa el operador 'o' de JavaScript.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L18 @E05 @solucion
Feature: L18 E05 · Un estado final malo también es final

  Scenario: Esperar a cualquier final
    * configure retry = { count: 40, interval: 500 }
    * def olof = call read('_login.feature') usuarios.olof
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '60.00' }
    When method post
    Then status 201
    * def deposito = response
    * def banco = call read('_iban.feature')
    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(banco.iban)', debtorName: 'Ana', amount: '60.00', reference: '#(deposito.reference)' }
    When method post
    Then status 201
    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until response.status == 'RECEIVED' || response.status == 'REJECTED'
    When method get
    Then status 200
    And match response.status == 'RECEIVED'
