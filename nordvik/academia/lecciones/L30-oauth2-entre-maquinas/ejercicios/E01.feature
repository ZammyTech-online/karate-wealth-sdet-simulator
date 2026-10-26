# ──────────────────────────────────────────────────────────────────────
# L30 · E01 · Client credentials
#
# Pide un token de máquina. Completa el tipo de concesión y cuánto dura el token.
#
# Rellena cada ___ y comprueba con:  academia corregir L30
# Pista: El flujo se llama igual que el tipo de credencial. La duración está en el README (en segundos).
# ──────────────────────────────────────────────────────────────────────
@L30 @E01
Feature: L30 E01 · Client credentials

  Scenario: Token
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = ___
    And form field client_id = 'nordvik'
    And form field client_secret = 'e11-insurer-client-secret-2026'
    When method post
    Then status 200
    And match response contains { token_type: 'Bearer', expires_in: ___ }
