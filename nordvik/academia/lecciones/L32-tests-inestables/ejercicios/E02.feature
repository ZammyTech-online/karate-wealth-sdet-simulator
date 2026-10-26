# ──────────────────────────────────────────────────────────────────────
# L32 · E02 · ¿Quién alterna?
#
# Tras cuatro ejecuciones, A pago alterna en cada una. Completa su historia y su tasa de alternancia.
#
# Rellena cada ___ y comprueba con:  academia corregir L32
# Pista: P es passed y F failed, en orden. Si cambia en las 3 transiciones posibles, la tasa es 1.
# ──────────────────────────────────────────────────────────────────────
@L32 @E02
Feature: L32 E02 · ¿Quién alterna?

  Scenario: Flaky
    * def suite = 'e02-' + java.util.UUID.randomUUID().toString().substring(0, 6)

    * def correr = function(suite, a, b){ return karate.http(testInsightsUrl + '/api/v1/runs').header('Content-Type', 'application/json').post({ suite: suite, results: [ { name: 'A pago', status: a, durationMs: 100 }, { name: 'B login', status: 'passed', durationMs: 40 }, { name: 'C orden', status: b, durationMs: 900 } ] }) }

    * correr(suite, 'passed', 'passed')
    * correr(suite, 'failed', 'passed')
    * correr(suite, 'passed', 'failed')
    * correr(suite, 'failed', 'failed')
    Given url testInsightsUrl
    And path '/api/v1/suites', suite, 'flaky'
    When method get
    Then status 200
    And match response.items[0].name == 'A pago'
    And match response.items[0].history == ___
    And match response.items[0].flipRate == ___
