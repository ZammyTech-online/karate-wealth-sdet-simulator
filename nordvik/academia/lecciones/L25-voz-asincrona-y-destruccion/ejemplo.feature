# Ejemplo resuelto de L25. Ejecútalo con:  academia ejemplo L25
@ejemplo
Feature: L25 · Una llamada corta de principio a fin

  Scenario: Llamar, esperar y comprobar el recibo
    * def c = call read('ejercicios/_cliente-con-grabacion.feature')
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
    Then status 200
    Given path '/api/v2/calls', id, 'destruction-receipt'
    And headers c.erik.auth
    When method get
    Then status 200
    Given path '/api/v2/receipts/verify'
    And request { receipt: '#(response.receipt)', signature: '#(response.signature)' }
    When method post
    Then status 200
    And match response.valid == true
