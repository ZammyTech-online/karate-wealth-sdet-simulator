# ──────────────────────────────────────────────────────────────────────
# L17 · E01 · Ana husmea a Lars
#
# Ana pide la ficha de Lars. Completa el estado y el código que protegen a Lars.
#
# Rellena cada ___ y comprueba con:  academia corregir L17
# Pista: Nordvik no confirma que Lars exista para Ana: responde como si no existiera.
# ──────────────────────────────────────────────────────────────────────
@L17 @E01
Feature: L17 E01 · Ana husmea a Lars

  Scenario: IDOR entre clientes
    * def ana = call read('_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers', ___
    And headers ana.auth
    When method get
    Then status ___
    And match response.code == ___
