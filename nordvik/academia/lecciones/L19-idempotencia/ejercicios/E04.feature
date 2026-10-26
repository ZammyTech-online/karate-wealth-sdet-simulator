# ──────────────────────────────────────────────────────────────────────
# L19 · E04 · Misma clave, otra orden
#
# Reutiliza la clave con otro importe. Completa el estado y el código.
#
# Rellena cada ___ y comprueba con:  academia corregir L19
# Pista: La petición está bien formada pero viola una regla: la clave ya se usó para otra cosa. Es un 422.
# ──────────────────────────────────────────────────────────────────────
@L19 @E04
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
    Then status ___
    And match response.code == ___
