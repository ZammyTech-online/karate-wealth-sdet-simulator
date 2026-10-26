# ──────────────────────────────────────────────────────────────────────
# L29 · E05 · Mandar una línea al mainframe
#
# Construye una línea BL de 80 columnas con el saldo 123456.78 del plan. Completa la longitud total y el código esperado.
#
# Rellena cada ___ y comprueba con:  academia corregir L29
# Pista: Toda línea mide lo mismo que el ancho del fichero. Si todo cuadra, cuatro ceros.
# ──────────────────────────────────────────────────────────────────────
@L29 @E05
Feature: L29 E05 · Mandar una línea al mainframe

  Scenario: Saldo heredado
    * def p = call read('_plan.feature')
    * def pad = function(s, n){ while (s.length < n) s = s + ' '; return s }
    * def linea = pad('BL' + pad(p.planId, 20) + '000000012345678' + '20141231', ___)
    Given url mainframeUrl
    And path '/api/v1/inbound/balances'
    And headers p.olof.auth
    And header Content-Type = 'text/plain'
    And request linea
    When method post
    Then status 200
    And match response.results[0].rc == ___
    Given path '/api/v1/legacy-balances', p.planId
    And headers p.olof.auth
    When method get
    Then match response.balance.amount == '123456.78'
