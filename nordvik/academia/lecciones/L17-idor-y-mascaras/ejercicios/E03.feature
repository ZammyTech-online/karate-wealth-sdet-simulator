# ──────────────────────────────────────────────────────────────────────
# L17 · E03 · La máscara del asesor
#
# Erik lee a Ana: comprueba que el número nacional sale enmascarado.
#
# Rellena cada ___ y comprueba con:  academia corregir L17
# Pista: Usa contains con el trozo de máscara: dos asteriscos, guion y cuatro asteriscos.
# ──────────────────────────────────────────────────────────────────────
@L17 @E03
Feature: L17 E03 · La máscara del asesor

  Scenario: Máscara
    * def erik = call read('_login.feature') usuarios.erik
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers erik.auth
    When method get
    Then status 200
    And match response.nationalId ___
