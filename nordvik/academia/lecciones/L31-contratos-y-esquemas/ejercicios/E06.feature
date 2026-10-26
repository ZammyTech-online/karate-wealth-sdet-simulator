# ──────────────────────────────────────────────────────────────────────
# L31 · E06 · La trampa T9
#
# Activa T9 y repite la comprobación. Completa el resultado y el campo nuevo que aparece sin documentar.
#
# Rellena cada ___ y comprueba con:  academia corregir L31
# Pista: Ahora no coincide: deriva. El equipo v2 renombró isin a otro nombre (mira el README).
# ──────────────────────────────────────────────────────────────────────
@L31 @E06
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
    And match response.status == ___
    And match response.differences contains { kind: 'UNDOCUMENTED_FIELD', path: ___, expected: '#string' }
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
