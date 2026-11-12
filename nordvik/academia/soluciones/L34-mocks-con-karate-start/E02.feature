# ──────────────────────────────────────────────────────────────────────
# L34 · E02 · Consultar el endpoint GET del mock
#
# Arranca el mock, apunta url a http://localhost:port, haz GET a /api/v1/mock/actuary y valida status 200.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# El mock responde inmediatamente con la lógica definida en sus scenarios.
# ──────────────────────────────────────────────────────────────────────
@L34 @E02 @solucion
Feature: L34 E02 · Consultar el endpoint GET del mock

  Scenario: E02 · Consumir mock GET
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    Given url 'http://localhost:' + mock.port
    And path '/api/v1/mock/actuary'
    When method get
    Then status 200
    * match response.status == 'APPROVED'
    * mock.stop()
