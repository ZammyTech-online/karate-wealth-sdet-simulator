# Ejemplo resuelto de L18. Ejecútalo con:  academia ejemplo L18
@ejemplo
Feature: L18 · Un ingreso de principio a fin

  Background:
    * def olof = callonce read('ejercicios/_login.feature') usuarios.olof
    * configure retry = { count: 40, interval: 500 }

  Scenario: Abrir el ingreso, transferir y esperar a que llegue
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '250.00' }
    When method post
    Then status 201
    And match response contains { status: 'AWAITING_FUNDS', reference: '#string' }
    * def deposito = response

    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    Then status 201
    * def iban = response.electronic

    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(iban)', debtorName: 'Ana', amount: '250.00', reference: '#(deposito.reference)' }
    When method post
    Then status 201

    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until response.status == 'RECEIVED'
    When method get
    Then status 200
    And match response.amount == '250.00'
