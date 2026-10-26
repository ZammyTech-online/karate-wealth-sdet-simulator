# Ejemplo resuelto de L07. Ejecútalo con:  academia ejemplo L07
@ejemplo
Feature: L07 · Crear, usar y borrar

  Background:
    * url sequenceUrl
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)

  Scenario: El ciclo de vida completo de una secuencia
    # Crear
    Given path '/api/v1/sequences'
    And request { name: '#(nombre)', prefix: 'CLI', padding: 4 }
    When method post
    Then status 201
    And match response.current == 0
    And match responseHeaders['Location'][0] == '/api/v1/sequences/' + nombre
    # Usar
    Given path '/api/v1/sequences', nombre, 'next'
    And param count = 2
    When method post
    Then status 200
    And match response.values == ['CLI-0001', 'CLI-0002']
    # Borrar
    Given path '/api/v1/sequences', nombre
    When method delete
    Then status 204
