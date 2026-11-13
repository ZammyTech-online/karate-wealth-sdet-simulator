@pedagogico @junior @funds
Feature: J-A: El Contrato HTTP en Sistemas Bancarios (GET Seguro vs Mutación)
  # [CONTEXTO ACTUARIAL]: En la gestora Nordvik, una consulta de catálogo jamás debe alterar
  # el patrimonio de un cliente. Los métodos GET son de solo lectura y seguros por contrato HTTP.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: [ARRANGE-ACT-ASSERT] Obtener catálogo público de fondos de inversión aprobados (HTTP 200)
    # [ARRANGE]: Definimos la ruta del recurso según la especificación OpenAPI
    Given path '/api/v1/funds'

    # [ACT]: Disparamos la petición HTTP de lectura
    When method get

    # [ASSERT]: Validamos el código de estado 200 y la integridad del catálogo
    Then status 200
    And match response.total == '#number'
    And match response.items == '#[]'
    And match response.items[0] contains { isin: '#string', name: '#string', nav: '#number', currency: 'SEK' }
