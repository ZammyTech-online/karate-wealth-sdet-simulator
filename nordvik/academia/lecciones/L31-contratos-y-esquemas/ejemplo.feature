# Ejemplo resuelto de L31. Ejecútalo con:  academia ejemplo L31
@ejemplo
Feature: L31 · Esquema y deriva

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post

  Scenario: El fondo cumple su esquema y su OpenAPI
    * def fondo = { isin: '#string', name: '#string', base_currency: '#string', sri: '#number', fees: '#[] #object' }
    Given url catalogUrl
    And path '/api/v1/funds/SE0000000001'
    When method get
    Then status 200
    And match response contains fondo
    Given url contractCheckUrl
    And path '/api/v1/checks'
    And request { service: 'product-catalog', path: '/api/v1/funds/SE0000000001' }
    When method post
    Then status 200
    And match response.status == 'MATCHES'
