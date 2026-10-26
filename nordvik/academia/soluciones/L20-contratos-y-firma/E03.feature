# ──────────────────────────────────────────────────────────────────────
# L20 · E03 · Lo que se firma es el hash
#
# Ana abre la sesión de firma. Comprueba que firma exactamente el contenido del contrato.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L20 @E03 @solucion
Feature: L20 E03 · Lo que se firma es el hash

  Scenario: Hash firmado
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
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
    And match response.status == 'PENDING'
    And match response.signedHash == contrato.contentHash
    # NordID solo admite una firma en curso por persona: esperamos a que acabe para no bloquear a la siguiente prueba
    * def sesion = response
    * configure retry = { count: 30, interval: 1000 }
    Given path '/api/v2/contracts', contrato.id, 'signing-sessions', sesion.id
    And headers ana.auth
    And retry until response.status != 'PENDING'
    When method get
    Then status 200
