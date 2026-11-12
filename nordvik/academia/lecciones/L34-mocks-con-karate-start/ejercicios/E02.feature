# ──────────────────────────────────────────────────────────────────────
# L34 · E02 · Consultar el endpoint GET del mock
#
# Arranca el mock, apunta url a http://localhost:port, haz GET a /api/v1/mock/actuary y valida status 200.
#
# Rellena cada ___ y comprueba con:  academia corregir L34
# Pista: Apunta la url al mockPort
# ──────────────────────────────────────────────────────────────────────
@L34 @E02
Feature: L34 E02 · Consultar el endpoint GET del mock

  Scenario: E02 · Consumir mock GET
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    Given url 'http://localhost:' + mock.port
    And path ___
    When method ___
    Then status ___
    * match response.status == ___
    * mock.stop()
