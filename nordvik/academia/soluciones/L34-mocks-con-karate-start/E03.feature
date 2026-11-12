# ──────────────────────────────────────────────────────────────────────
# L34 · E03 · Enviar POST al mock y verificar respuesta 201
#
# Haz un POST a /api/v1/mock/actuary enviando un cuerpo JSON { orderId: 'ORD-1' } y valida status 201.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Los mocks permiten probar que el cliente maneja respuestas 201 con payloads complejos.
# ──────────────────────────────────────────────────────────────────────
@L34 @E03 @solucion
Feature: L34 E03 · Enviar POST al mock y verificar respuesta 201

  Scenario: E03 · POST a mock
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    Given url 'http://localhost:' + mock.port
    And path '/api/v1/mock/actuary'
    And request { orderId: 'ORD-1' }
    When method post
    Then status 201
    * match response.result == 'COMMITTED'
    * mock.stop()
