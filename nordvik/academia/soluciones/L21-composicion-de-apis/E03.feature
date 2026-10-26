# ──────────────────────────────────────────────────────────────────────
# L21 · E03 · Las fuentes
#
# Comprueba la lista de servicios de los que sale el bundle, en orden.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L21 @E03 @solucion
Feature: L21 E03 · Las fuentes

  Scenario: Fuentes
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
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.sources[*].service == ['contract-service', 'document-vault']
    # contains: cada fuente trae además fetchedAt y ms, que cambian en cada llamada
    And match each response.sources contains { service: '#string', version: '#number', status: 200 }
