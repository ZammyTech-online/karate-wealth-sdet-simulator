# ──────────────────────────────────────────────────────────────────────
# L25 · E01 · Sin consentimiento no hay llamada
#
# Un cliente que no ha consentido la grabación. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L25 @E01 @solucion
Feature: L25 E01 · Sin consentimiento no hay llamada

  Scenario: Sin consentimiento
    * def c = call read('_cliente.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status 409
    And match response.code == 'RECORDING_CONSENT_MISSING'
