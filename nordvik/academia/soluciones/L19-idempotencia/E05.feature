# ──────────────────────────────────────────────────────────────────────
# L19 · E05 · Los errores también se repiten
#
# Una orden sin fondos falla. Repetida con la misma clave devuelve el mismo error, marcado como repetición.
# Completa el código y el valor de la cabecera.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L19 @E05 @solucion
Feature: L19 E05 · Los errores también se repiten

  Background:
    * def cliente = callonce read('_cliente-con-dinero.feature') { importe: '1000.00' }

  Scenario: Error repetido
    * def clave = 'k-' + java.util.UUID.randomUUID()
    * def orden = { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '5000.00' }
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request orden
    When method post
    Then status 422
    And match response.code == 'INSUFFICIENT_FUNDS'
    Given path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request orden
    When method post
    Then status 422
    And match karate.response.header('idempotent-replayed') == 'true'
