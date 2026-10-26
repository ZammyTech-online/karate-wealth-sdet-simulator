# ──────────────────────────────────────────────────────────────────────
# L31 · E05 · OpenAPI frente a la realidad
#
# Pide a contract-check que compare product-catalog. Completa el servicio y el resultado esperado sin trampas.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L31 @E05 @solucion
Feature: L31 E05 · OpenAPI frente a la realidad

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post

  Scenario: Sin deriva
    Given url contractCheckUrl
    And path '/api/v1/checks'
    And request { service: 'product-catalog', path: '/api/v1/funds/SE0000000001' }
    When method post
    Then status 200
    And match response.status == 'MATCHES'
