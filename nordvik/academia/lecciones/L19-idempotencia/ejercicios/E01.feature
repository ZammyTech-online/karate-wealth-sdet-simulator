# ──────────────────────────────────────────────────────────────────────
# L19 · E01 · Sin clave no hay orden
#
# Manda una orden sin Idempotency-Key. Completa el estado y el código.
#
# Rellena cada ___ y comprueba con:  academia corregir L19
# Pista: Es un error de la petición (falta algo), no de negocio. El código dice que la clave es obligatoria.
# ──────────────────────────────────────────────────────────────────────
@L19 @E01
Feature: L19 E01 · Sin clave no hay orden

  Scenario: Falta la clave
    * def erik = call read('_login.feature') usuarios.erik
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers erik.auth
    And request { portfolioId: '#(ids.carteraAna)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
    When method post
    Then status ___
    And match response.code == ___
