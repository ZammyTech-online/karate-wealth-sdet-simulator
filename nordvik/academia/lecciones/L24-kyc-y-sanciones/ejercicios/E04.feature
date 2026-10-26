# ──────────────────────────────────────────────────────────────────────
# L24 · E04 · El asesor no decide
#
# Erik intenta resolver una coincidencia. Completa estado y código.
#
# Rellena cada ___ y comprueba con:  academia corregir L24
# Pista: Erik es quien es, pero decidir es cosa de Cumplimiento: falta el rol.
# ──────────────────────────────────────────────────────────────────────
@L24 @E04
Feature: L24 E04 · El asesor no decide

  Scenario: Decide Cumplimiento
    * def karin = call read('_login.feature') usuarios.karin
    * def erik = call read('_login.feature') usuarios.erik
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Viktor Draganov', birthDate: '1961-04-12' }
    When method post
    Then status 201
    Given path '/api/v1/screenings', response.id, 'decision'
    And headers erik.auth
    And request { decision: 'FALSE_POSITIVE', reason: 'No es él' }
    When method post
    Then status ___
    And match response.code == ___
