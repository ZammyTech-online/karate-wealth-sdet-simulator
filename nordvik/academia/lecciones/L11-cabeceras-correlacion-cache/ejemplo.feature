# Ejemplo resuelto de L11. Ejecútalo con:  academia ejemplo L11
@ejemplo
Feature: L11 · Cabeceras

  Scenario: Mi correlationId vuelve en la respuesta
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    And header X-Correlation-Id = 'academia-l11-ejemplo'
    When method get
    Then status 200
    And match responseHeaders['X-Correlation-Id'][0] == 'academia-l11-ejemplo'
    And match responseHeaders['X-Service'][0] == 'market-data'

  Scenario: Caché con ETag y 304
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    * def etag = responseHeaders['ETag'][0]
    Given path '/api/v1/prices', 'SE0000000001', 'latest'
    And header If-None-Match = etag
    When method get
    Then status 304
