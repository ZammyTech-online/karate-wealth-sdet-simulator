# ──────────────────────────────────────────────────────────────────────
# L29 · E06 · Una fecha imposible
#
# El mes 13 no existe. Completa la fecha mala (AAAAMMDD) y el código de retorno.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L29 @E06 @solucion
Feature: L29 E06 · Una fecha imposible

  Scenario: Fecha imposible
    * def p = call read('_plan.feature')
    * def pad = function(s, n){ while (s.length < n) s = s + ' '; return s }
    * def linea = pad('BL' + pad(p.planId, 20) + '000000012345678' + '20141301', 80)
    Given url mainframeUrl
    And path '/api/v1/inbound/balances'
    And headers p.olof.auth
    And header Content-Type = 'text/plain'
    And request linea
    When method post
    Then status 200
    And match response.results[0].rc == '0042'
