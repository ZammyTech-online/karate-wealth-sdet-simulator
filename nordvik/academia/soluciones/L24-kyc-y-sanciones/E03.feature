# ──────────────────────────────────────────────────────────────────────
# L24 · E03 · La puntuación por encima del umbral
#
# Valida la primera coincidencia con un marcador que acepte cualquier puntuación >= 85.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L24 @E03 @solucion
Feature: L24 E03 · La puntuación por encima del umbral

  Scenario: Puntuación
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Draganov Viktor' }
    When method post
    Then status 201
    And match response.matches[0] contains { entryId: 'EU-0001', score: '#? _ >= 85' }
