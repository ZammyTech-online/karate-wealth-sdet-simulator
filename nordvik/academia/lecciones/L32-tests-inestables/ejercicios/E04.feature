# ──────────────────────────────────────────────────────────────────────
# L32 · E04 · La tendencia
#
# Tres ejecuciones cada vez peores. Completa la dirección de la tendencia y la tasa de éxito de la primera.
#
# Rellena cada ___ y comprueba con:  academia corregir L32
# Pista: Si la última es peor que la primera, empeora. La primera pasó todo: 100 %.
# ──────────────────────────────────────────────────────────────────────
@L32 @E04
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
    And match response.direction == ___
    And match response.runs[0].passRate == ___
