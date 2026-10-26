# ──────────────────────────────────────────────────────────────────────
# L18 · E02 · La condición de espera
#
# Escribe la condición: espera hasta que el ingreso esté RECEIVED.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L18 @E02 @solucion
Feature: L18 E02 · La condición de espera

  Scenario: Esperar al ingreso
    * configure retry = { count: 40, interval: 500 }
    * def olof = call read('_login.feature') usuarios.olof
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '120.00' }
    When method post
    Then status 201
    * def deposito = response
    * def banco = call read('_iban.feature')
    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(banco.iban)', debtorName: 'Ana', amount: '120.00', reference: '#(deposito.reference)' }
    When method post
    Then status 201
    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until response.status == 'RECEIVED'
    When method get
    Then status 200
    And match response.amount == '120.00'
