# ──────────────────────────────────────────────────────────────────────
# L20 · E04 · Esperar a NordID
#
# La firma tarda unos segundos. Escribe la condición: espera mientras siga PENDING.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L20 @E04 @solucion
Feature: L20 E04 · Esperar a NordID

  Scenario: Firma completa
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    * configure retry = { count: 30, interval: 1000 }
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def contrato = response
    Given path '/api/v2/contracts', contrato.id, 'send'
    And headers erik.auth
    And request {}
    When method post
    Then status 200
    Given path '/api/v2/contracts', contrato.id, 'signing-sessions'
    And headers ana.auth
    And request { party: 'CUSTOMER' }
    When method post
    Then status 201
    * def sesion = response
    Given path '/api/v2/contracts', contrato.id, 'signing-sessions', sesion.id
    And headers ana.auth
    And retry until response.status != 'PENDING'
    When method get
    Then status 200
    And match response.status == 'COMPLETED'
