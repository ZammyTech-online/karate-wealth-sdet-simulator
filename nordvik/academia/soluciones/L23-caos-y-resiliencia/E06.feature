# ──────────────────────────────────────────────────────────────────────
# L23 · E06 · Un simulador caído
#
# Tumba el simulador de listas de sanciones y comprueba que Nordvik lo dice claro. Completa el modo y el código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L23 @E06 @solucion
Feature: L23 E06 · Un simulador caído

  Background:
    Given url mockControlUrl
    And path '/api/v1/mocks/reset'
    When method post
    Then status 200

  Scenario: Proveedor caído
    * def karin = call read('_login.feature') usuarios.karin
    Given url mockControlUrl
    And path '/api/v1/mocks/sanctionscreen-sim/behaviour'
    And request { mode: 'down' }
    When method put
    Then status 200
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Persona Corriente' }
    When method post
    Then status 503
    And match response.code == 'SCREENING_PROVIDER_DOWN'
    Given url mockControlUrl
    And path '/api/v1/mocks/reset'
    When method post
    Then status 200
