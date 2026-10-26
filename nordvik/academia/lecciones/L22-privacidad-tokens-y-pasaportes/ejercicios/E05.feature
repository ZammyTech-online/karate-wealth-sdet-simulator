# ──────────────────────────────────────────────────────────────────────
# L22 · E05 · Un asesor no ve el dato en claro
#
# Erik intenta destokenizar con una finalidad válida. Completa estado y código.
#
# Rellena cada ___ y comprueba con:  academia corregir L22
# Pista: Sabemos quién es y la finalidad es válida, pero su rol no puede. El código empieza por DETOKENIZE.
# ──────────────────────────────────────────────────────────────────────
@L22 @E05
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
    Then status ___
    And match response.code == ___
