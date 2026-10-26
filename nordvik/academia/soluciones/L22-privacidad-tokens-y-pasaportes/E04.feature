# ──────────────────────────────────────────────────────────────────────
# L22 · E04 · Sin finalidad no hay dato
#
# Karin (Cumplimiento) pide ver el dato en claro sin decir para qué. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L22 @E04 @solucion
Feature: L22 E04 · Sin finalidad no hay dato

  Scenario: Falta purpose
    * def karin = call read('_login.feature') usuarios.karin
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'PHONE', value: '+46 70 555 12 34', customerId: '#(ids.ana)' }
    When method post
    Then assert responseStatus == 200 || responseStatus == 201
    * def token = response.token
    Given path '/api/v1/tokens', token, 'detokenize'
    And headers karin.auth
    And request {}
    When method post
    Then status 400
    And match response.code == 'PURPOSE_REQUIRED'
