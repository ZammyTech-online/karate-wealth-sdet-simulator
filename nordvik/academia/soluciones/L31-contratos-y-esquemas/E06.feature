# ──────────────────────────────────────────────────────────────────────
# L31 · E06 · La trampa T9
#
# Activa T9 y repite la comprobación. Completa el resultado y el campo nuevo que aparece sin documentar.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L31 @E06 @solucion
Feature: L31 E06 · La trampa T9

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post

  Scenario: Deriva detectada
    Given url chaosUrl
    And path '/api/v1/traps/T9'
    And request { enabled: true }
    When method put
    Then status 200
    Given url contractCheckUrl
    And path '/api/v1/checks'
    And request { service: 'product-catalog', path: '/api/v1/funds/SE0000000001' }
    When method post
    Then status 200
    And match response.status == 'DRIFT'
    And match response.differences contains { kind: 'UNDOCUMENTED_FIELD', path: '$.instrumentId', expected: '#string' }
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
