# ──────────────────────────────────────────────────────────────────────
# L13 · E03 · Usar lo que devuelve
#
# El ayudante guarda el nombre que inventó en su variable nombre. Recupéralo y pide el siguiente valor.
# Sin prefijo, el ayudante usa GEN.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L13 @E03 @solucion
Feature: L13 E03 · Usar lo que devuelve

  Scenario: Usar la secuencia creada
    * def resultado = call read('_crear-secuencia.feature')
    * def nombre = resultado.nombre
    Given url sequenceUrl
    And path '/api/v1/sequences', nombre, 'next'
    When method post
    Then status 200
    And match response.values == ['GEN-0001']
