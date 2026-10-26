# Ejemplo resuelto de L13. Ejecútalo con:  academia ejemplo L13
@ejemplo
Feature: L13 · Llamar a una feature reutilizable

  Scenario: Crear una secuencia con el ayudante y usarla
    * def resultado = call read('ejercicios/_crear-secuencia.feature') { prefix: 'CLI' }
    * match resultado.response.prefix == 'CLI'
    * def nombre = resultado.nombre
    Given url sequenceUrl
    And path '/api/v1/sequences', nombre, 'next'
    When method post
    Then status 200
    And match response.values == ['CLI-0001']
