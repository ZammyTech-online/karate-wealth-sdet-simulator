# ──────────────────────────────────────────────────────────────────────
# L07 · E06 · Borrar y comprobar
#
# Borra la secuencia que acabas de crear. Completa el método y su código, que no devuelve cuerpo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L07 @E06 @solucion
Feature: L07 E06 · Borrar y comprobar

  Scenario: Borrar una secuencia
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method post
    Then status 201
    Given path '/api/v1/sequences', nombre
    When method delete
    Then status 204
    Given path '/api/v1/sequences', nombre
    When method get
    Then status 404
