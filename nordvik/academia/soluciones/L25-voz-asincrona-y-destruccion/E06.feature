# ──────────────────────────────────────────────────────────────────────
# L25 · E06 · El número dictado no se transcribe
#
# El cliente dicta su número nacional. Completa el estado al que llega la llamada y lo que aparece en su lugar.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L25 @E06 @solucion
Feature: L25 E06 · El número dictado no se transcribe

  Scenario: Redacción en la transcripción
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
    And request { scriptId: 'v3-dicta-numero', variables: { PNR: '#(c.nationalId)' } }
    When method post
    Then status 202
    Given path '/api/v2/calls', id, 'end'
    And headers c.erik.auth
    When method post
    Then status 202
    * configure retry = { count: 40, interval: 500 }
    Given path '/api/v2/calls', id
    And headers c.erik.auth
    And retry until response.status == 'AWAITING_REVIEW'
    When method get
    Given path '/api/v2/calls', id, 'transcript'
    And headers c.erik.auth
    When method get
    Then status 200
    And match response.turns[1].text contains '[NATIONAL_ID]'
    And match karate.toString(response) !contains c.nationalId
