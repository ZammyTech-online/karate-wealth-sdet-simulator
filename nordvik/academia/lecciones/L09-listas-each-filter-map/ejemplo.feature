# Ejemplo resuelto de L09. Ejecútalo con:  academia ejemplo L09
@ejemplo
Feature: L09 · Trabajar con listas

  Background:
    * url catalogUrl
    * path '/api/v1/funds'

  Scenario: Todos los fondos de renta variable tienen riesgo alto
    Given param category = 'EQUITY'
    When method get
    Then status 200
    And match each response.items contains { category: 'EQUITY' }
    And match each response.items contains { sri: '#? _ >= 4' }

  Scenario: Filtrar y transformar
    When method get
    Then status 200
    * def caros = karate.filter(response.items, function(f){ return parseFloat(f.management_fee) > 1 })
    * match caros == '#[4]'
    * def nombres = karate.map(caros, function(f){ return f.name })
    * match nombres contains 'Nordvik Tech Growth'
    * print 'Fondos con comisión mayor del 1 %:', nombres
