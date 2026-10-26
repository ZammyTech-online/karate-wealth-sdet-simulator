# ──────────────────────────────────────────────────────────────────────
# L20 · E01 · Nace un borrador
#
# Erik crea un contrato marco para Ana. Completa el tipo y el estado inicial.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L20 @E01 @solucion
Feature: L20 E01 · Nace un borrador

  Scenario: Crear
    * def erik = call read('_login.feature') usuarios.erik
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    And match response.status == 'DRAFT'
    And match response.contentHash == '#regex [0-9a-f]{64}'
