# ──────────────────────────────────────────────────────────────────────
# L25 · E04 · Lo destruido no vuelve
#
# Tras la destrucción, la transcripción ya no se puede pedir. Completa estado y código.
#
# Rellena cada ___ y comprueba con:  academia corregir L25
# Pista: Ya no hay transcripción que devolver. El código dice que no está disponible.
# ──────────────────────────────────────────────────────────────────────
@L25 @E04
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
    Then status ___
    And match response.code == ___
