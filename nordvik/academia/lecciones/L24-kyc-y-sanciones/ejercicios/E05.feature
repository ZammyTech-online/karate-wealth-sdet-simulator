# ──────────────────────────────────────────────────────────────────────
# L24 · E05 · Se decide una vez
#
# Karin confirma la coincidencia y luego intenta cambiar de opinión. Completa el status tras decidir y el estado HTTP del segundo intento.
#
# Rellena cada ___ y comprueba con:  academia corregir L24
# Pista: TRUE_MATCH confirma la coincidencia. Una vez decidido no queda nada que decidir: conflicto.
# ──────────────────────────────────────────────────────────────────────
@L24 @E05
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
    And match response.status == ___
    Given path '/api/v1/screenings', id, 'decision'
    And headers karin.auth
    And request { decision: 'FALSE_POSITIVE', reason: 'Cambio de opinión' }
    When method post
    Then status ___
    And match response.code == 'NOTHING_TO_DECIDE'
