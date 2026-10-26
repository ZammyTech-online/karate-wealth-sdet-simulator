# ──────────────────────────────────────────────────────────────────────
# L12 · E03 · Viajar a Nochebuena
#
# Viaja al 24 de diciembre y pregunta si hoy es hábil. ¿Qué razón da el calendario?
#
# Rellena cada ___ y comprueba con:  academia corregir L12
# Pista: No es fin de semana: es otra razón. Mira los valores posibles de reason en Swagger.
# ──────────────────────────────────────────────────────────────────────
@L12 @E03
Feature: L12 E03 · Viajar a Nochebuena

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Nochebuena no es día hábil
    Given url clockUrl
    And path '/api/v1/clock/travel'
    And request { to: '2026-12-24T10:00:00+01:00' }
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/is-trading-day'
    When method get
    Then status 200
    And match response.tradingDay == false
    And match response.reason == ___
