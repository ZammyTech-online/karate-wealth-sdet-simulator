# ──────────────────────────────────────────────────────────────────────
# L07 · E04 · Crear dos veces lo mismo
#
# Intenta crear dos veces la misma secuencia. ¿Qué código devuelve el segundo intento?
#
# Rellena cada ___ y comprueba con:  academia corregir L07
# Pista: No es que la petición esté mal ni que algo no exista: choca con algo que ya existe.
# ──────────────────────────────────────────────────────────────────────
@L07 @E04
Feature: L07 E04 · Crear dos veces lo mismo

  Scenario: Un nombre repetido
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method post
    Then status 201
    Given path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method post
    Then status ___
    And match response.code == 'SEQUENCE_ALREADY_EXISTS'
