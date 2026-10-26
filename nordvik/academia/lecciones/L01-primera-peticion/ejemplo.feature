# Ejemplo resuelto de L01. Ejecútalo con:  academia ejemplo L01
@ejemplo
Feature: L01 · Las cuatro piezas de una petición

  Scenario: Detalle de un fondo con la ruta en trozos
    * def isin = 'SE0000000002'
    Given url catalogUrl
    And path '/api/v1/funds', isin
    When method get
    Then status 200

  Scenario: El último precio de un fondo en otro microservicio
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200

  Scenario: La salud de un servicio
    Given url fxUrl
    And path '/health'
    When method get
    Then status 200
