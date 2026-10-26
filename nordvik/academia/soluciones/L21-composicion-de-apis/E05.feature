# ──────────────────────────────────────────────────────────────────────
# L21 · E05 · La composición respeta la seguridad
#
# Lars pide el bundle de Ana y alguien lo pide sin token. Completa los dos estados.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L21 @E05 @solucion
Feature: L21 E05 · La composición respeta la seguridad

  Scenario: Seguridad heredada
    * def erik = call read('_login.feature') usuarios.erik
    * def lars = call read('_login.feature') usuarios.lars
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers lars.auth
    When method get
    Then status 404
    Given path '/api/v2/compositions/contract-bundle', id
    When method get
    Then status 401
    And match response.code == 'AUTHENTICATION_REQUIRED'
