# ──────────────────────────────────────────────────────────────────────
# L07 · E02 · El método para crear y su código
#
# Completa el método que crea recursos y el código que devuelve el servidor cuando crea algo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L07 @E02 @solucion
Feature: L07 E02 · El método para crear y su código

  Scenario: Crear devuelve 201
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method post
    Then status 201
