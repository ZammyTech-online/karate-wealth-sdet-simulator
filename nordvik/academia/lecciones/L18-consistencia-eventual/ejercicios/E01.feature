# ──────────────────────────────────────────────────────────────────────
# L18 · E01 · Cuántas veces reintentar
#
# Configura 40 intentos cada medio segundo (20 s de techo).
#
# Rellena cada ___ y comprueba con:  academia corregir L18
# Pista: count es el número de intentos, interval los milisegundos entre ellos.
# ──────────────────────────────────────────────────────────────────────
@L18 @E01
Feature: L18 E01 · Cuántas veces reintentar

  Scenario: Configurar el reintento
    * configure retry = { count: ___, interval: ___ }
    * def olof = call read('_login.feature') usuarios.olof
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '100.00' }
    When method post
    Then status 201
    * def deposito = response
    * def banco = call read('_iban.feature')
    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(banco.iban)', debtorName: 'Ana', amount: '100.00', reference: '#(deposito.reference)' }
    When method post
    Then status 201
    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until response.status == 'RECEIVED'
    When method get
    Then status 200
