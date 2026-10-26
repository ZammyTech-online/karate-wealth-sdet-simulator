# ──────────────────────────────────────────────────────────────────────
# L24 · E05 · Se decide una vez
#
# Karin confirma la coincidencia y luego intenta cambiar de opinión. Completa el status tras decidir y el estado HTTP del segundo intento.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L24 @E05 @solucion
Feature: L24 E05 · Se decide una vez

  Scenario: Decisión única
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Viktor Draganov', birthDate: '1961-04-12' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v1/screenings', id, 'decision'
    And headers karin.auth
    And request { decision: 'TRUE_MATCH', reason: 'Mismo nombre y fecha' }
    When method post
    Then status 200
    And match response.status == 'CONFIRMED_MATCH'
    Given path '/api/v1/screenings', id, 'decision'
    And headers karin.auth
    And request { decision: 'FALSE_POSITIVE', reason: 'Cambio de opinión' }
    When method post
    Then status 409
    And match response.code == 'NOTHING_TO_DECIDE'
