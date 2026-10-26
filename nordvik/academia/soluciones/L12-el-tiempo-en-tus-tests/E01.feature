# ──────────────────────────────────────────────────────────────────────
# L12 · E01 · Antes de la hora de corte
#
# Congelamos el reloj a las 14:59 de un lunes hábil. ¿Estamos antes de la hora de corte?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L12 @E01 @solucion
Feature: L12 E01 · Antes de la hora de corte

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: A las 14:59 todavía se aceptan órdenes del día
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T14:59:00+01:00' }
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == true
