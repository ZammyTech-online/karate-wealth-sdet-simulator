# Ejemplo resuelto de L23. Ejecútalo con:  academia ejemplo L23
@ejemplo
Feature: L23 · Caos

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: Un 503 solo para esta petición
    Given url uuidUrl
    And path '/health'
    And header X-Chaos-Scenario = 'status=503'
    When method get
    Then status 503
    And match response.code == 'CHAOS_INJECTED'

  Scenario: Una regla de dos fallos y un reintento que la supera
    Given url chaosUrl
    And path '/api/v1/faults'
    And request { service: 'uuid-service', pathPrefix: '/api/v1/uuids', status: 503, times: 2 }
    When method post
    Then status 201
    * configure retry = { count: 5, interval: 100 }
    Given url uuidUrl
    And path '/api/v1/uuids'
    And retry until responseStatus == 200
    When method get
    Then status 200
