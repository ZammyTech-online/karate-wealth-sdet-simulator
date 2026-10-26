# ──────────────────────────────────────────────────────────────────────
# L12 · E05 · El NAV ya se ha publicado
#
# Ahora son las 18:00 en punto del mismo lunes. ¿De qué fecha es el último precio?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L12 @E05 @solucion
Feature: L12 E05 · El NAV ya se ha publicado

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: A las 18:00 ya está el precio de hoy
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T18:00:00+01:00' }
    When method post
    Then status 200
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    And match response.date == '2026-10-26'
