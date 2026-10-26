# ──────────────────────────────────────────────────────────────────────
# L32 · E01 · Tu primera ejecución
#
# Registra una ejecución con un resultado. Completa el estado y el número de ejecución de una suite nueva.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L32 @E01 @solucion
Feature: L32 E01 · Tu primera ejecución

  Scenario: Una ejecución
    * def suite = 'e01-' + java.util.UUID.randomUUID().toString().substring(0, 6)
    Given url testInsightsUrl
    And path '/api/v1/runs'
    And request { suite: '#(suite)', results: [ { name: 'L16 E01', status: 'passed', durationMs: 340 } ] }
    When method post
    Then status 201
    And match response.runNumber == 1
