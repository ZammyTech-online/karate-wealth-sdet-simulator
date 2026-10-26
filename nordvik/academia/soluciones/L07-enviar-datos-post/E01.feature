# ──────────────────────────────────────────────────────────────────────
# L07 · E01 · Tu primer cuerpo JSON
#
# Crea una secuencia con el prefijo CLI. El nombre único ya está en la variable nombre.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L07 @E01 @solucion
Feature: L07 E01 · Tu primer cuerpo JSON

  Scenario: Crear una secuencia con prefijo
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)', prefix: 'CLI' }
    When method post
    Then status 201
    And match response.prefix == 'CLI'
