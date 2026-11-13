@pedagogico @senior @orders @async
Feature: S-A: Sondeo Inteligente sin Sleep para Órdenes Asíncronas (retry until)
  # [CONTEXTO ACTUARIAL]: La compensación de órdenes en bolsa toma entre 2 y 5 segundos.
  # Usar Thread.sleep() hace que los tests sean lentos y frágiles. Usamos 'retry until'.

  Background:
    * url 'http://localhost:5125'
    * configure retry = { count: 10, interval: 500 }
    * header Accept = 'application/json'

  Scenario: Crear orden de compra de participaciones y esperar confirmación del custodio
    # Paso 1: Enviamos la orden de compra
    Given path '/api/v1/orders'
    And request { isin: 'SE0001', units: 25.5, accountId: 'ACC-8801' }
    When method post
    Then status 201
    * def orderId = response.orderId

    # Paso 2: Hacemos sondeo no bloqueante hasta que el custodio liquide la orden
    Given path '/api/v1/orders/' + orderId
    And retry until response.status == 'SETTLED'
    When method get
    Then status 200
    And match response.settledUnits == 25.5
