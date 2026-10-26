# ──────────────────────────────────────────────────────────────────────
# L17 · E01 · Ana husmea a Lars
#
# Ana pide la ficha de Lars. Completa el estado y el código que protegen a Lars.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L17 @E01 @solucion
Feature: L17 E01 · Ana husmea a Lars

  Scenario: IDOR entre clientes
    * def ana = call read('_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers', ids.lars
    And headers ana.auth
    When method get
    Then status 404
    And match response.code == 'CUSTOMER_NOT_FOUND'
