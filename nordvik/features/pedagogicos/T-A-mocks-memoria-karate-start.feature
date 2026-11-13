@pedagogico @staff @mock
Feature: T-A: Servidores Mock en Memoria para Simulación de Custodios Internacionales
  # [CONTEXTO ACTUARIAL]: Las pruebas no pueden depender de la disponibilidad de SWIFT o Euroclear.
  # Un Staff SDET levanta un microservicio Mock HTTP en memoria en el puerto dinámico de Karate.

  Background:
    # Levantamos un mock HTTP que responderá a la liquidación de la orden
    * def port = karate.env == 'ci' ? 8999 : 8998
    * def mockServer = karate.start({ mock: 'classpath:mocks/custodian-mock.feature', port: port })
    * url 'http://localhost:' + port

  Scenario: Validar intercambio de mensajes MT541 con custodio simulado
    Given path '/swift/settlement'
    And request { instructionType: 'MT541', isin: 'SE0001', volume: 1000 }
    When method post
    Then status 200
    And match response.acknowledgement == 'ACK_MT541_PROCESSED'
    # Detenemos el servidor mock de forma limpia
    * mockServer.stop()
