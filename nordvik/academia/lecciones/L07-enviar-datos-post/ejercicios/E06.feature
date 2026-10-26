# ──────────────────────────────────────────────────────────────────────
# L07 · E06 · Borrar y comprobar
#
# Borra la secuencia que acabas de crear. Completa el método y su código, que no devuelve cuerpo.
#
# Rellena cada ___ y comprueba con:  academia corregir L07
# Pista: El método de borrar se llama igual que la acción en inglés. Su código es 'hecho, sin contenido'.
# ──────────────────────────────────────────────────────────────────────
@L07 @E06
Feature: L07 E06 · Borrar y comprobar

  Scenario: Borrar una secuencia
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method post
    Then status 201
    Given path '/api/v1/sequences', nombre
    When method ___
    Then status ___
    Given path '/api/v1/sequences', nombre
    When method get
    Then status 404
