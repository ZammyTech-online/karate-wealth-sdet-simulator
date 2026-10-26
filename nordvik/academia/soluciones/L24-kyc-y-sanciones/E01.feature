# ──────────────────────────────────────────────────────────────────────
# L24 · E01 · Limpio
#
# Comprueba a 'Persona Corriente'. Completa el estado HTTP y el status del resultado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L24 @E01 @solucion
Feature: L24 E01 · Limpio

  Scenario: Persona corriente
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Persona Corriente' }
    When method post
    Then status 201
    And match response.status == 'CLEAR'
