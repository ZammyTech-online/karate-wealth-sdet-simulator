# ──────────────────────────────────────────────────────────────────────
# L12 · E05 · El NAV ya se ha publicado
#
# Ahora son las 18:00 en punto del mismo lunes. ¿De qué fecha es el último precio?
#
# Rellena cada ___ y comprueba con:  academia corregir L12
# Pista: A las 18:00 se publica el del propio día.
# ──────────────────────────────────────────────────────────────────────
@L12 @E05
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
    And match response.date == ___
