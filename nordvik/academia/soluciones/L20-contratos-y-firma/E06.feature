# ──────────────────────────────────────────────────────────────────────
# L20 · E06 · Las cláusulas vienen ordenadas
#
# Comprueba que todas las cláusulas tienen número, código y texto, y que la primera es la GEN-01.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L20 @E06 @solucion
Feature: L20 E06 · Las cláusulas vienen ordenadas

  Scenario: Cláusulas
    * def erik = call read('_login.feature') usuarios.erik
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    And match each response.content.clauses == { clauseNo: '#number', code: '#string', title: '#string', text: '#string' }
    And match response.content.clauses[0].code == 'GEN-01'
