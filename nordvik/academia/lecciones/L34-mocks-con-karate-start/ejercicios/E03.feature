# ──────────────────────────────────────────────────────────────────────
# L34 · E03 · Enviar POST al mock y verificar respuesta 201
#
# Haz un POST a /api/v1/mock/actuary enviando un cuerpo JSON { orderId: 'ORD-1' } y valida status 201.
#
# Rellena cada ___ y comprueba con:  academia corregir L34
# Pista: Usa method post y Then status 201
# ──────────────────────────────────────────────────────────────────────
@L34 @E03
Feature: L34 E03 · Enviar POST al mock y verificar respuesta 201

  Scenario: E03 · POST a mock
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    Given url 'http://localhost:' + mock.port
    And path '/api/v1/mock/actuary'
    And request ___
    When method ___
    Then status ___
    * match response.result == ___
    * mock.stop()
