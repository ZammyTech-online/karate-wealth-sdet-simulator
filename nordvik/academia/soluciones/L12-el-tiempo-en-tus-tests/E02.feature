# ──────────────────────────────────────────────────────────────────────
# L12 · E02 · Avanzar el reloj
#
# Desde las 14:59, avanza el reloj lo justo para pasar la hora de corte: hasta las 15:01.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L12 @E02 @solucion
Feature: L12 E02 · Avanzar el reloj

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: A las 15:01 la orden ya va al día siguiente
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T14:59:00+01:00' }
    When method post
    Then status 200
    Given path '/api/v1/clock/advance'
    And request { minutes: 2 }
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == false
