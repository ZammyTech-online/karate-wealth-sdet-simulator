# ──────────────────────────────────────────────────────────────────────
# L10 · E01 · Rellena la tabla
#
# Completa el valor esperado de las dos últimas filas: Navidad y un sábado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L10 @E01 @solucion
Feature: L10 E01 · Rellena la tabla

  Scenario Outline: ¿Es hábil el <fecha>?
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/is-trading-day'
    And param date = '<fecha>'
    When method get
    Then status 200
    And match response.tradingDay == <habil>

    Examples:
      | fecha      | habil |
      | 2026-10-26 | true  |
      | 2026-12-25 | false |
      | 2026-10-24 | false |
