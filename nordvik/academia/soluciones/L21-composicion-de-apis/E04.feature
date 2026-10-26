# ──────────────────────────────────────────────────────────────────────
# L21 · E04 · La traza, solo si la pides
#
# Pide la composición con traza y sin ella. Completa la cabecera y el marcador de 'no existe'.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L21 @E04 @solucion
Feature: L21 E04 · La traza, solo si la pides

  Scenario: Traza
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
    And header X-Composition-Trace = 'true'
    When method get
    Then status 200
    And match response.trace == '#object'
    Given path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.trace == '#notpresent'
