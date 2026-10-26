# ──────────────────────────────────────────────────────────────────────
# L19 · E04 · Misma clave, otra orden
#
# Reutiliza la clave con otro importe. Completa el estado y el código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L19 @E04 @solucion
Feature: L19 E04 · Misma clave, otra orden

  Background:
    * def cliente = callonce read('_cliente-con-dinero.feature') { importe: '1000.00' }

  Scenario: Clave reutilizada
    * def clave = 'k-' + java.util.UUID.randomUUID()
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
    When method post
    Then status 201
    Given path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '301.00' }
    When method post
    Then status 422
    And match response.code == 'IDEMPOTENCY_KEY_REUSED'
