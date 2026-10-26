# ──────────────────────────────────────────────────────────────────────
# L19 · E01 · Sin clave no hay orden
#
# Manda una orden sin Idempotency-Key. Completa el estado y el código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L19 @E01 @solucion
Feature: L19 E01 · Sin clave no hay orden

  Scenario: Falta la clave
    * def erik = call read('_login.feature') usuarios.erik
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers erik.auth
    And request { portfolioId: '#(ids.carteraAna)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
    When method post
    Then status 400
    And match response.code == 'IDEMPOTENCY_KEY_REQUIRED'
