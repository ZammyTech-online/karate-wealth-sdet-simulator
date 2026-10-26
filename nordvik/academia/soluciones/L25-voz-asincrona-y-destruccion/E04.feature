# ──────────────────────────────────────────────────────────────────────
# L25 · E04 · Lo destruido no vuelve
#
# Tras la destrucción, la transcripción ya no se puede pedir. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L25 @E04 @solucion
Feature: L25 E04 · Lo destruido no vuelve

  Scenario: Transcripción destruida
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
    * configure retry = { count: 40, interval: 500 }
    Given path '/api/v2/calls', id
    And headers c.erik.auth
    And retry until response.status == 'SHREDDED'
    When method get
    Given path '/api/v2/calls', id, 'transcript'
    And headers c.erik.auth
    When method get
    Then status 404
    And match response.code == 'TRANSCRIPT_NOT_AVAILABLE'
