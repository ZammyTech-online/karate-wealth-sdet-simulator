# Ejemplo de L15: un escenario resuelto como modelo de los demás.
@ejemplo
Feature: L15 · Modelo de escenario de la suite

  Scenario: Divisa coherente entre catálogo y precios para el fondo global
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    * def divisa = response.base_currency
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    And match response.currency == divisa
