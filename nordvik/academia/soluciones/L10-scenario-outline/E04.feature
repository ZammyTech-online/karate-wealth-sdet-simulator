# ──────────────────────────────────────────────────────────────────────
# L10 · E04 · T+N en una tabla
#
# Una orden del viernes 23 de octubre se liquida en T+2. ¿Qué día es?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L10 @E04 @solucion
Feature: L10 E04 · T+N en una tabla

  Scenario Outline: <desde> más <n> días hábiles
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/next-trading-day'
    And params { date: '<desde>', offset: <n> }
    When method get
    Then status 200
    And match response.date == '<esperado>'

    Examples:
      | desde      | n | esperado   |
      | 2026-10-23 | 2 | 2026-10-27 |
      | 2026-12-23 | 1 | 2026-12-28 |
      | 2026-10-26 | 1 | 2026-10-27 |
