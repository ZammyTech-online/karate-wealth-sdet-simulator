# ──────────────────────────────────────────────────────────────────────
# L10 · E05 · Los datos, en un fichero
#
# Los festivos suecos están en festivos.csv, en la misma carpeta. Léelo como tabla de Examples.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L10 @E05 @solucion
Feature: L10 E05 · Los datos, en un fichero

  Scenario Outline: <nombre> no es día hábil en Estocolmo
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/is-trading-day'
    And param date = '<fecha>'
    When method get
    Then status 200
    And match response.tradingDay == false
    And match response.holidayName == '<nombre>'

    Examples:
      | read('festivos.csv') |
