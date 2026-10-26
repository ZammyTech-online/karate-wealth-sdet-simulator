# Ejemplo resuelto de L12. Ejecútalo con:  academia ejemplo L12
@ejemplo
Feature: L12 · Controlar el tiempo

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Un minuto antes y un minuto después de la hora de corte
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
    Given url clockUrl
    And path '/api/v1/clock/advance'
    And request { minutes: 2 }
    When method post
    Then status 200
    And match response.time == '15:01:00'
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == false
