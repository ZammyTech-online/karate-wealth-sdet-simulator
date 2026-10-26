# ──────────────────────────────────────────────────────────────────────
# L24 · E01 · Limpio
#
# Comprueba a 'Persona Corriente'. Completa el estado HTTP y el status del resultado.
#
# Rellena cada ___ y comprueba con:  academia corregir L24
# Pista: Una comprobación nueva se crea. Nadie se parece a esa persona.
# ──────────────────────────────────────────────────────────────────────
@L24 @E01
Feature: L24 E01 · Limpio

  Scenario: Persona corriente
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Persona Corriente' }
    When method post
    Then status ___
    And match response.status == ___
