# Ejemplo resuelto de L10. Ejecútalo con:  academia ejemplo L10
@ejemplo
Feature: L10 · Una prueba, muchos casos

  Scenario Outline: El <fecha> en <mercado> es hábil: <habil>
    Given url calendarUrl
    And path '/api/v1/calendar', '<mercado>', 'is-trading-day'
    And param date = '<fecha>'
    When method get
    Then status 200
    And match response.tradingDay == <habil>

    Examples:
      | mercado | fecha      | habil |
      | XSTO    | 2026-10-26 | true  |
      | XSTO    | 2026-06-06 | false |
      | XOSL    | 2026-06-06 | false |
      | XOSL    | 2026-05-18 | true  |
      | XCSE    | 2026-06-05 | false |
