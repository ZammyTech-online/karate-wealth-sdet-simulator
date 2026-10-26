# ──────────────────────────────────────────────────────────────────────
# L30 · E01 · Client credentials
#
# Pide un token de máquina. Completa el tipo de concesión y cuánto dura el token.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L30 @E01 @solucion
Feature: L30 E01 · Client credentials

  Scenario: Token
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = 'client_credentials'
    And form field client_id = 'nordvik'
    And form field client_secret = 'e11-insurer-client-secret-2026'
    When method post
    Then status 200
    And match response contains { token_type: 'Bearer', expires_in: 300 }
