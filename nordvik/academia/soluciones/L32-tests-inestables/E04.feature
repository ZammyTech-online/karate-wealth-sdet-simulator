# ──────────────────────────────────────────────────────────────────────
# L32 · E04 · La tendencia
#
# Tres ejecuciones cada vez peores. Completa la dirección de la tendencia y la tasa de éxito de la primera.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L32 @E04 @solucion
Feature: L32 E04 · La tendencia

  Scenario: Empeora
    * def suite = 'e04-' + java.util.UUID.randomUUID().toString().substring(0, 6)

    * def correr = function(suite, a, b){ return karate.http(testInsightsUrl + '/api/v1/runs').header('Content-Type', 'application/json').post({ suite: suite, results: [ { name: 'A pago', status: a, durationMs: 100 }, { name: 'B login', status: 'passed', durationMs: 40 }, { name: 'C orden', status: b, durationMs: 900 } ] }) }

    * correr(suite, 'passed', 'passed')
    * correr(suite, 'failed', 'passed')
    * correr(suite, 'failed', 'failed')
    Given url testInsightsUrl
    And path '/api/v1/suites', suite, 'trend'
    When method get
    Then status 200
    And match response.direction == 'WORSENING'
    And match response.runs[0].passRate == 100.0
