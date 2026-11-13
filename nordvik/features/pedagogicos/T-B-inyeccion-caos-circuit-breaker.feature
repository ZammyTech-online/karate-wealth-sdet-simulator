@pedagogico @staff @chaos @resilience
Feature: T-B: Inyección de Caos en el Proveedor de Precios y Activación de Circuit Breaker
  # [CONTEXTO ACTUARIAL]: Si el feed de precios de la Bolsa de Estocolmo (OMX) sufre un 50% de latencia,
  # el Gateway debe abrir el Circuit Breaker y responder con el precio del último cierre sin caerse.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Inyectar latencia de 3000ms y verificar fallback a precio de cierre anterior
    # Activamos caos en el microservicio de precios
    Given path '/api/v1/chaos/latency'
    And request { targetService: 'price-feed', latencyMs: 3000, enabled: true }
    When method post
    Then status 200

    # Petición del cliente: El circuit breaker debe activarse ante timeout y devolver fallback
    Given path '/api/v1/market/prices/SE0001'
    When method get
    Then status 200
    And match response.source == 'CIRCUIT_BREAKER_FALLBACK'
    And match response.priceType == 'LAST_KNOWN_CLOSE'

    # Restauramos el estado del cluster
    Given path '/api/v1/chaos/latency'
    And request { targetService: 'price-feed', enabled: false }
    When method post
    Then status 200
