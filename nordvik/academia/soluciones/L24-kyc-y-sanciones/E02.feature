# ──────────────────────────────────────────────────────────────────────
# L24 · E02 · Un error de tecleo no te salva
#
# 'Viktor Draganow' (con w) también salta. Completa el status esperado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L24 @E02 @solucion
Feature: L24 E02 · Un error de tecleo no te salva

  Scenario: Casi el mismo nombre
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Viktor Draganow', birthDate: '1961-04-12' }
    When method post
    Then status 201
    And match response.status == 'POTENTIAL_MATCH'
