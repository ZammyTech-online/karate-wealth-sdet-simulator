# ──────────────────────────────────────────────────────────────────────
# L30 · E06 · Lo que hace Nordvik por dentro
#
# El adaptador de Nordvik nunca enseña su token. Completa el campo que dice si tiene uno y el operador que comprueba que el token no aparece.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L30 @E06 @solucion
Feature: L30 E06 · Lo que hace Nordvik por dentro

  Scenario: Estado del token del adaptador
    * def olof = call read('_login.feature') usuarios.olof
    Given url insurerAdapterUrl
    And path '/api/v1/token-status'
    And headers olof.auth
    When method get
    Then status 200
    And match response.hasToken == '#boolean'
    * def texto = karate.toString(response)
    * match texto !contains 'ins_'
