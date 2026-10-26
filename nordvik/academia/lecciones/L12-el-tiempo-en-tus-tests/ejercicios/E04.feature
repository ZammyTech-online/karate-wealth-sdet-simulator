# ──────────────────────────────────────────────────────────────────────
# L12 · E04 · El NAV todavía no se ha publicado
#
# Son las 17:59 del lunes 26 de octubre. El NAV del día se publica a las 18:00.
# ¿De qué fecha es el último precio publicado?
#
# Rellena cada ___ y comprueba con:  academia corregir L12
# Pista: Si el de hoy aún no está, el último es el del día hábil anterior. El 26 es lunes.
# ──────────────────────────────────────────────────────────────────────
@L12 @E04
Feature: L12 E04 · El NAV todavía no se ha publicado

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: A las 17:59 el último precio es el del viernes
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T17:59:00+01:00' }
    When method post
    Then status 200
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    And match response.date == ___
