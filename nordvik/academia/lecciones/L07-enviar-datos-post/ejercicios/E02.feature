# ──────────────────────────────────────────────────────────────────────
# L07 · E02 · El método para crear y su código
#
# Completa el método que crea recursos y el código que devuelve el servidor cuando crea algo.
#
# Rellena cada ___ y comprueba con:  academia corregir L07
# Pista: Para crear no se usa get. Y el código no es 200: es el de 'creado'.
# ──────────────────────────────────────────────────────────────────────
@L07 @E02
Feature: L07 E02 · El método para crear y su código

  Scenario: Crear devuelve 201
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method ___
    Then status ___
