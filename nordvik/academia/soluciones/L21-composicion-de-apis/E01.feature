# ──────────────────────────────────────────────────────────────────────
# L21 · E01 · Sin anexo, parcial
#
# Un contrato sin anexo da una composición parcial. Completa el estado y lo que falta.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L21 @E01 @solucion
Feature: L21 E01 · Sin anexo, parcial

  Scenario: Parcial
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.status == 'PARTIAL'
    And match response.missing == ['annex']
