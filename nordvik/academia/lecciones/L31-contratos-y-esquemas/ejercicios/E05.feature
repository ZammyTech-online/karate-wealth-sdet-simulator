# ──────────────────────────────────────────────────────────────────────
# L31 · E05 · OpenAPI frente a la realidad
#
# Pide a contract-check que compare product-catalog. Completa el servicio y el resultado esperado sin trampas.
#
# Rellena cada ___ y comprueba con:  academia corregir L31
# Pista: El servicio se llama por su nombre (no por su puerto). Si todo cuadra, coincide.
# ──────────────────────────────────────────────────────────────────────
@L31 @E05
Feature: L31 E05 · OpenAPI frente a la realidad

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post

  Scenario: Sin deriva
    Given url contractCheckUrl
    And path '/api/v1/checks'
    And request { service: ___, path: '/api/v1/funds/SE0000000001' }
    When method post
    Then status 200
    And match response.status == ___
