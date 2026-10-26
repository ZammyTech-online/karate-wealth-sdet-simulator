# Ayudante reutilizable de L13: crea una secuencia con nombre único.
# Recibe (opcional) prefix. Devuelve: nombre y response.
@ignore
Feature: Crear una secuencia con nombre único

  Scenario: Crear secuencia
    * def prefijo = karate.get('prefix', 'GEN')
    * def nombre = 'l13-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)', prefix: '#(prefijo)' }
    When method post
    Then status 201
