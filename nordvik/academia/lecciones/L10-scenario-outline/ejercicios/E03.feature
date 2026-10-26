# ──────────────────────────────────────────────────────────────────────
# L10 · E03 · Fiestas nacionales
#
# Completa el nombre de la fiesta nacional de Noruega (17 de mayo). Míralo en
# GET /api/v1/calendar/XOSL/holidays?year=2026.
#
# Rellena cada ___ y comprueba con:  academia corregir L10
# Pista: Es el día de la Constitución. Copia el nombre exacto de la respuesta.
# ──────────────────────────────────────────────────────────────────────
@L10 @E03
Feature: L10 E03 · Fiestas nacionales

  Scenario Outline: <mercado> celebra el <fecha>
    Given url calendarUrl
    And path '/api/v1/calendar', '<mercado>', 'is-trading-day'
    And param date = '<fecha>'
    When method get
    Then status 200
    And match response.holidayName == '<nombre>'

    Examples:
      | mercado | fecha      | nombre |
      | XOSL    | 2026-05-17 | ___ |
      | XCSE    | 2026-06-05 | Día de la Constitución de Dinamarca |
      | XHEL    | 2026-12-06 | Día de la Independencia de Finlandia |
