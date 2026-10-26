# ──────────────────────────────────────────────────────────────────────
# L20 · E05 · Un contrato ajeno
#
# Lars intenta leer el contrato de Ana. Completa el estado y el código (recuerda L17).
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L20 @E05 @solucion
Feature: L20 E05 · Un contrato ajeno

  Scenario: IDOR de contratos
    * def erik = call read('_login.feature') usuarios.erik
    * def lars = call read('_login.feature') usuarios.lars
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def contrato = response
    Given path '/api/v2/contracts', contrato.id
    And headers lars.auth
    When method get
    Then status 404
    And match response.code == 'CONTRACT_NOT_FOUND'
