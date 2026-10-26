# ──────────────────────────────────────────────────────────────────────
# L29 · E06 · Una fecha imposible
#
# El mes 13 no existe. Completa la fecha mala (AAAAMMDD) y el código de retorno.
#
# Rellena cada ___ y comprueba con:  academia corregir L29
# Pista: Cualquier fecha con mes 13 vale. El rc de fecha inválida está en GET /api/v1/return-codes.
# ──────────────────────────────────────────────────────────────────────
@L29 @E06
Feature: L29 E06 · Una fecha imposible

  Scenario: Fecha imposible
    * def p = call read('_plan.feature')
    * def pad = function(s, n){ while (s.length < n) s = s + ' '; return s }
    * def linea = pad('BL' + pad(p.planId, 20) + '000000012345678' + ___, 80)
    Given url mainframeUrl
    And path '/api/v1/inbound/balances'
    And headers p.olof.auth
    And header Content-Type = 'text/plain'
    And request linea
    When method post
    Then status 200
    And match response.results[0].rc == ___
