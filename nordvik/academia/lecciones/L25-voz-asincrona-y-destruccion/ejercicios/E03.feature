# ──────────────────────────────────────────────────────────────────────
# L25 · E03 · Espera al final del pipeline
#
# Completa la condición de espera y el número de trozos de audio que quedan guardados al final.
#
# Rellena cada ___ y comprueba con:  academia corregir L25
# Pista: La llamada corta termina destruida. Destruida quiere decir que no queda ningún trozo.
# ──────────────────────────────────────────────────────────────────────
@L25 @E03
Feature: L25 E03 · Espera al final del pipeline

  Scenario: Hasta la destrucción
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
    And retry until ___
    When method get
    Then status 200
    And match response.storedChunks == ___
