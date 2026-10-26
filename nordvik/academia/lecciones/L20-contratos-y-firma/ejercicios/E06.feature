# ──────────────────────────────────────────────────────────────────────
# L20 · E06 · Las cláusulas vienen ordenadas
#
# Comprueba que todas las cláusulas tienen número, código y texto, y que la primera es la GEN-01.
#
# Rellena cada ___ y comprueba con:  academia corregir L20
# Pista: match each recorre la lista. La primera cláusula es la posición 0.
# ──────────────────────────────────────────────────────────────────────
@L20 @E06
Feature: L20 E06 · Las cláusulas vienen ordenadas

  Scenario: Cláusulas
    * def erik = call read('_login.feature') usuarios.erik
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    And ___ response.content.clauses == { clauseNo: '#number', code: '#string', title: '#string', text: '#string' }
    And match response.___ == 'GEN-01'
