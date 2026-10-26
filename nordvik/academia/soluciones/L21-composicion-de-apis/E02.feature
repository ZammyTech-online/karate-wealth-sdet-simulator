# ──────────────────────────────────────────────────────────────────────
# L21 · E02 · La unión exacta
#
# Con anexo, el bundle contiene el contrato tal cual. Completa la comparación.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L21 @E02 @solucion
Feature: L21 E02 · La unión exacta

  Scenario: Unión exacta
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'ADVISORY_MANDATE', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/contracts', id, 'annexes'
    And headers erik.auth
    And request { kind: 'FEE_SCHEDULE', content: { managementFee: '0.45', entryFee: '0.00', currency: 'SEK' } }
    When method post
    Then status 201
    Given path '/api/v2/contracts', id
    And headers ana.auth
    When method get
    Then status 200
    * def contrato = response
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.status == 'COMPLETE'
    And match response.bundle.contract == contrato
