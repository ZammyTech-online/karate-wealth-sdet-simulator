# ──────────────────────────────────────────────────────────────────────
# L22 · E05 · Un asesor no ve el dato en claro
#
# Erik intenta destokenizar con una finalidad válida. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L22 @E05 @solucion
Feature: L22 E05 · Un asesor no ve el dato en claro

  Scenario: Erik no destokeniza
    * def c = call read('_cliente.feature')
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    Given path '/api/v1/tokens', response.token, 'detokenize'
    And headers c.erik.auth
    And request { purpose: 'KYC_REVIEW' }
    When method post
    Then status 403
    And match response.code == 'DETOKENIZE_NOT_ALLOWED'
