# ──────────────────────────────────────────────────────────────────────
# L25 · E02 · Aceptado no es terminado
#
# Lanza el guion y cuelga. Completa los dos estados HTTP.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L25 @E02 @solucion
Feature: L25 E02 · Aceptado no es terminado

  Scenario: Guion y colgar
    * def c = call read('_cliente-con-grabacion.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/calls', id, 'play-script'
    And headers c.erik.auth
    And request { scriptId: 'v2-consulta-corta' }
    When method post
    Then status 202
    Given path '/api/v2/calls', id, 'end'
    And headers c.erik.auth
    When method post
    Then status 202
