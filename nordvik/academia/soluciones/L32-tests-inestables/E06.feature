# ──────────────────────────────────────────────────────────────────────
# L32 · E06 · Nombres repetidos
#
# Dos resultados con el mismo nombre en una ejecución no se aceptan. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L32 @E06 @solucion
Feature: L32 E06 · Nombres repetidos

  Scenario: Duplicados
    Given url testInsightsUrl
    And path '/api/v1/runs'
    And request { suite: 'dup', results: [ { name: 'X', status: 'passed' }, { name: 'X', status: 'failed' } ] }
    When method post
    Then status 422
    And match response.code == 'DUPLICATE_TEST_NAMES'
