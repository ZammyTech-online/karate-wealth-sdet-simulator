# Ejemplo resuelto de L19. Ejecútalo con:  academia ejemplo L19
@ejemplo
Feature: L19 · Idempotencia de órdenes

  Background:
    * def cliente = callonce read('ejercicios/_cliente-con-dinero.feature') { importe: '1000.00' }
    * def orden = { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }

  Scenario: Dos envíos, una sola compra
    * def clave = 'k-' + java.util.UUID.randomUUID()
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request orden
    When method post
    Then status 201
    * def primera = response

    Given path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request orden
    When method post
    Then status 201
    And match response.id == primera.id
    And match karate.response.header('idempotent-replayed') == 'true'

    * configure retry = { count: 20, interval: 500 }
    Given url cashUrl
    And path '/api/v1/internal/customers', cliente.customerId, 'cash'
    And retry until response.available == '700.00'
    When method get
    Then status 200
