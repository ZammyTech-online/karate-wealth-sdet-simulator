# ──────────────────────────────────────────────────────────────────────
# L07 · E03 · Pedir los siguientes valores
#
# Crea una secuencia con prefijo A y pide 2 valores. Completa el último trozo de la ruta
# y la lista de valores que esperas (relleno de 4 cifras).
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L07 @E03 @solucion
Feature: L07 E03 · Pedir los siguientes valores

  Scenario: Dos valores seguidos
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)', prefix: 'A' }
    When method post
    Then status 201
    Given path '/api/v1/sequences', nombre, 'next'
    And param count = 2
    When method post
    Then status 200
    And match response.values == ['A-0001', 'A-0002']
