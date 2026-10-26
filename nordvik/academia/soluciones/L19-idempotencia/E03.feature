# ──────────────────────────────────────────────────────────────────────
# L19 · E03 · La repetición devuelve lo mismo
#
# Manda la misma orden dos veces con la misma clave. Completa la cabecera y la comparación de ids.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L19 @E03 @solucion
Feature: L19 E03 · La repetición devuelve lo mismo

  Background:
    * def cliente = callonce read('_cliente-con-dinero.feature') { importe: '1000.00' }

  Scenario: Repetición
    * def clave = 'k-' + java.util.UUID.randomUUID()
    * def orden = { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
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
