# ──────────────────────────────────────────────────────────────────────
# L10 · E01 · Rellena la tabla
#
# Completa el valor esperado de las dos últimas filas: Navidad y un sábado.
#
# Rellena cada ___ y comprueba con:  academia corregir L10
# Pista: ¿Abre la bolsa en Navidad? ¿Y un sábado? Los booleanos van sin comillas.
# ──────────────────────────────────────────────────────────────────────
@L10 @E01
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
      | 2026-12-25 | ___ |
      | 2026-10-24 | ___ |
