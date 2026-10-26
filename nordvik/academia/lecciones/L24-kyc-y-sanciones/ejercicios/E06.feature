# ──────────────────────────────────────────────────────────────────────
# L24 · E06 · Parcial nunca es limpio
#
# Pon el simulador a responder solo algunas listas. Completa el status del resultado.
#
# Rellena cada ___ y comprueba con:  academia corregir L24
# Pista: No es CLEAR aunque nadie coincida: el proveedor no miró todo.
# ──────────────────────────────────────────────────────────────────────
@L24 @E06
Feature: L24 E06 · Parcial nunca es limpio

  Scenario: Respuesta parcial
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionSimUrl
    And path '/__admin/partial'
    And param on = true
    When method post
    Then status 200
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Persona Corriente' }
    When method post
    * def resultado = response
    Given url sanctionSimUrl
    And path '/__admin/partial'
    And param on = false
    When method post
    Then status 200
    * match resultado.status == ___
    * match resultado.partial == true
