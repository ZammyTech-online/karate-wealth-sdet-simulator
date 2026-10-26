# ──────────────────────────────────────────────────────────────────────
# L10 · E02 · Usar la columna en un paso
#
# Completa el parámetro date con la columna fecha de la tabla.
#
# Rellena cada ___ y comprueba con:  academia corregir L10
# Pista: La columna se escribe entre < >. Como la fecha es un texto, lleva comillas alrededor.
# ──────────────────────────────────────────────────────────────────────
@L10 @E02
Feature: L10 E02 · Usar la columna en un paso

  Scenario Outline: Días hábiles de finales de octubre
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/is-trading-day'
    And param date = ___
    When method get
    Then status 200
    And match response.tradingDay == true

    Examples:
      | fecha      |
      | 2026-10-26 |
      | 2026-10-27 |
      | 2026-10-28 |
