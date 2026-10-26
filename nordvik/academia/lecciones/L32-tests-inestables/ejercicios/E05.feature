# ──────────────────────────────────────────────────────────────────────
# L32 · E05 · El más lento
#
# Completa el nombre del test más lento y el recurso de la URL que lo da.
#
# Rellena cada ___ y comprueba con:  academia corregir L32
# Pista: C orden tarda 900 ms. El recurso es el superlativo de lento en inglés.
# ──────────────────────────────────────────────────────────────────────
@L32 @E05
Feature: L32 E05 · El más lento

  Scenario: Lentos
    * def suite = 'e05-' + java.util.UUID.randomUUID().toString().substring(0, 6)

    * def correr = function(suite, a, b){ return karate.http(testInsightsUrl + '/api/v1/runs').header('Content-Type', 'application/json').post({ suite: suite, results: [ { name: 'A pago', status: a, durationMs: 100 }, { name: 'B login', status: 'passed', durationMs: 40 }, { name: 'C orden', status: b, durationMs: 900 } ] }) }

    * correr(suite, 'passed', 'passed')
    Given url testInsightsUrl
    And path '/api/v1/suites', suite, ___
    When method get
    Then status 200
    And match response.items[0].name == ___
