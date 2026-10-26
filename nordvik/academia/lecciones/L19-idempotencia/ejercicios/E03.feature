# ──────────────────────────────────────────────────────────────────────
# L19 · E03 · La repetición devuelve lo mismo
#
# Manda la misma orden dos veces con la misma clave. Completa la cabecera y la comparación de ids.
#
# Rellena cada ___ y comprueba con:  academia corregir L19
# Pista: La cabecera se llama Idempotency-Key. La segunda respuesta tiene el mismo id que la primera.
# ──────────────────────────────────────────────────────────────────────
@L19 @E03
Feature: L19 E03 · La repetición devuelve lo mismo

  Background:
    * def cliente = callonce read('_cliente-con-dinero.feature') { importe: '1000.00' }

  Scenario: Repetición
    * def clave = 'k-' + java.util.UUID.randomUUID()
    * def orden = { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers cliente.erik.auth
    And header ___ = clave
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
    And match response.id == ___
    And match karate.response.header('idempotent-replayed') == 'true'
