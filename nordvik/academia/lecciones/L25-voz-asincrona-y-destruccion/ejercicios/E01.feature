# ──────────────────────────────────────────────────────────────────────
# L25 · E01 · Sin consentimiento no hay llamada
#
# Un cliente que no ha consentido la grabación. Completa estado y código.
#
# Rellena cada ___ y comprueba con:  academia corregir L25
# Pista: La petición es correcta, pero choca con el estado del cliente: conflicto.
# ──────────────────────────────────────────────────────────────────────
@L25 @E01
Feature: L25 E01 · Sin consentimiento no hay llamada

  Scenario: Sin consentimiento
    * def c = call read('_cliente.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status ___
    And match response.code == ___
