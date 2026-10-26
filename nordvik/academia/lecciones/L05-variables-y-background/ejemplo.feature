# Ejemplo resuelto de L05. Ejecútalo con:  academia ejemplo L05
@ejemplo
Feature: L05 · Variables y Background

  Background:
    * url marketUrl
    * def isin = 'SE0000000001'

  Scenario: El último precio, guardado en variables
    Given path '/api/v1/prices', isin, 'latest'
    When method get
    Then status 200
    * def nav = response.nav
    * def navNumero = parseFloat(nav)
    * assert navNumero > 0
    * print 'El NAV de', isin, 'es', nav

  Scenario: El precio de una fecha construida con una expresión
    * def mes = '03'
    * def fecha = '2026-' + mes + '-02'
    Given path '/api/v1/prices', isin
    And param date = fecha
    When method get
    Then status 200
    And match response.date == fecha
