# ──────────────────────────────────────────────────────────────────────
# L21 · E04 · La traza, solo si la pides
#
# Pide la composición con traza y sin ella. Completa la cabecera y el marcador de 'no existe'.
#
# Rellena cada ___ y comprueba con:  academia corregir L21
# Pista: La cabecera se llama X-Composition-Trace. El marcador de Karate para un campo ausente empieza por #not.
# ──────────────────────────────────────────────────────────────────────
@L21 @E04
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
    And header ___ = 'true'
    When method get
    Then status 200
    And match response.trace == '#object'
    Given path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.trace == ___
