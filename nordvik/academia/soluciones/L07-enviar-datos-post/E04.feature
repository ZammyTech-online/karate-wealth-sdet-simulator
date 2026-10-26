# ──────────────────────────────────────────────────────────────────────
# L07 · E04 · Crear dos veces lo mismo
#
# Intenta crear dos veces la misma secuencia. ¿Qué código devuelve el segundo intento?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L07 @E04 @solucion
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
    Then status 409
    And match response.code == 'SEQUENCE_ALREADY_EXISTS'
