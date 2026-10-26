# ──────────────────────────────────────────────────────────────────────
# L12 · E06 · Dejar el reloj como estaba
#
# Congela el reloj y después devuélvelo a la hora real. ¿Qué modo indica el reloj?
#
# Rellena cada ___ y comprueba con:  academia corregir L12
# Pista: Los modos son REAL, OFFSET y FROZEN.
# ──────────────────────────────────────────────────────────────────────
@L12 @E06
Feature: L12 E06 · Dejar el reloj como estaba

  Scenario: Volver a la hora real
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2030-01-01T00:00:00+01:00' }
    When method post
    Then status 200
    And match response.mode == 'FROZEN'
    Given path '/api/v1/clock/reset'
    And request {}
    When method post
    Then status 200
    And match response.mode == ___
