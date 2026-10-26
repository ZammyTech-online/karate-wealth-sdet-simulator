# ──────────────────────────────────────────────────────────────────────
# L13 · E03 · Usar lo que devuelve
#
# El ayudante guarda el nombre que inventó en su variable nombre. Recupéralo y pide el siguiente valor.
# Sin prefijo, el ayudante usa GEN.
#
# Rellena cada ___ y comprueba con:  academia corregir L13
# Pista: call devuelve un objeto con todas las variables del ayudante. Accede con un punto.
# ──────────────────────────────────────────────────────────────────────
@L13 @E03
Feature: L13 E03 · Usar lo que devuelve

  Scenario: Usar la secuencia creada
    * def resultado = call read('_crear-secuencia.feature')
    * def nombre = resultado.___
    Given url sequenceUrl
    And path '/api/v1/sequences', nombre, 'next'
    When method post
    Then status 200
    And match response.values == ['GEN-0001']
