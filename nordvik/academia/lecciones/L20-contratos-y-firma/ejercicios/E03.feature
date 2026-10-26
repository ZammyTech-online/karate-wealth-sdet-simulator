# ──────────────────────────────────────────────────────────────────────
# L20 · E03 · Lo que se firma es el hash
#
# Ana abre la sesión de firma. Comprueba que firma exactamente el contenido del contrato.
#
# Rellena cada ___ y comprueba con:  academia corregir L20
# Pista: La parte que firma es el cliente. El signedHash de la sesión es el contentHash del contrato.
# ──────────────────────────────────────────────────────────────────────
@L20 @E03
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
    And request { party: ___ }
    When method post
    Then status 201
    And match response.status == 'PENDING'
    And match response.signedHash == ___
    # NordID solo admite una firma en curso por persona: esperamos a que acabe para no bloquear a la siguiente prueba
    * def sesion = response
    * configure retry = { count: 30, interval: 1000 }
    Given path '/api/v2/contracts', contrato.id, 'signing-sessions', sesion.id
    And headers ana.auth
    And retry until response.status != 'PENDING'
    When method get
    Then status 200
