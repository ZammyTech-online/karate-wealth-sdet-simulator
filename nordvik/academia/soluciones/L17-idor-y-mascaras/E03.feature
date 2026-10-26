# ──────────────────────────────────────────────────────────────────────
# L17 · E03 · La máscara del asesor
#
# Erik lee a Ana: comprueba que el número nacional sale enmascarado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L17 @E03 @solucion
Feature: L17 E03 · La máscara del asesor

  Scenario: Máscara
    * def erik = call read('_login.feature') usuarios.erik
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers erik.auth
    When method get
    Then status 200
    And match response.nationalId contains '**-****'
