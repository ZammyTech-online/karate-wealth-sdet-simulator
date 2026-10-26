# ──────────────────────────────────────────────────────────────────────
# L10 · E05 · Los datos, en un fichero
#
# Los festivos suecos están en festivos.csv, en la misma carpeta. Léelo como tabla de Examples.
#
# Rellena cada ___ y comprueba con:  academia corregir L10
# Pista: read() lee ficheros relativos a la carpeta del .feature. El nombre va entre comillas.
# ──────────────────────────────────────────────────────────────────────
@L10 @E05
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
      | read(___) |
