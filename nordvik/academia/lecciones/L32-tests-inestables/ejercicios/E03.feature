# ──────────────────────────────────────────────────────────────────────
# L32 · E03 · Lo estable no es inestable
#
# B login pasa siempre. Completa el operador que comprueba que no aparece en la lista de inestables.
#
# Rellena cada ___ y comprueba con:  academia corregir L32
# Pista: Quieres decir 'la lista NO contiene un elemento con ese nombre'.
# ──────────────────────────────────────────────────────────────────────
@L32 @E03
Feature: L32 E03 · Lo estable no es inestable

  Scenario: Estable
    * def suite = 'e03-' + java.util.UUID.randomUUID().toString().substring(0, 6)

    * def correr = function(suite, a, b){ return karate.http(testInsightsUrl + '/api/v1/runs').header('Content-Type', 'application/json').post({ suite: suite, results: [ { name: 'A pago', status: a, durationMs: 100 }, { name: 'B login', status: 'passed', durationMs: 40 }, { name: 'C orden', status: b, durationMs: 900 } ] }) }

    * correr(suite, 'passed', 'passed')
    * correr(suite, 'failed', 'passed')
    * correr(suite, 'passed', 'failed')
    Given url testInsightsUrl
    And path '/api/v1/suites', suite, 'flaky'
    When method get
    Then status 200
    And match response.items[*].name ___ 'B login'
