# ──────────────────────────────────────────────────────────────────────
# L12 · E06 · Dejar el reloj como estaba
#
# Congela el reloj y después devuélvelo a la hora real. ¿Qué modo indica el reloj?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L12 @E06 @solucion
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
    And match response.mode == 'REAL'
