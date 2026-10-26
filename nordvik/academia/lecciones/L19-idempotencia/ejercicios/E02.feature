# ──────────────────────────────────────────────────────────────────────
# L19 · E02 · Una clave demasiado corta
#
# La clave 'corta' tiene 5 caracteres. Completa el código del error.
#
# Rellena cada ___ y comprueba con:  academia corregir L19
# Pista: La clave existe pero no tiene el formato válido.
# ──────────────────────────────────────────────────────────────────────
@L19 @E02
Feature: L19 E02 · Una clave demasiado corta

  Scenario: Clave inválida
    * def erik = call read('_login.feature') usuarios.erik
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers erik.auth
    And header Idempotency-Key = 'corta'
    And request { portfolioId: '#(ids.carteraAna)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
    When method post
    Then status 400
    And match response.code == ___
