# ──────────────────────────────────────────────────────────────────────
# L25 · E05 · Un recibo que no se puede falsificar
#
# Cambia una letra del recibo: la verificación tiene que decir que no vale. Completa los dos resultados.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L25 @E05 @solucion
Feature: L25 E05 · Un recibo que no se puede falsificar

  Scenario: Recibo firmado
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
    Given path '/api/v2/calls', id, 'destruction-receipt'
    And headers c.erik.auth
    When method get
    Then status 200
    * def recibo = response
    Given path '/api/v2/receipts/verify'
    And request { receipt: '#(recibo.receipt)', signature: '#(recibo.signature)' }
    When method post
    Then match response.valid == true
    * def falso = recibo.receipt.replace(id, id + 'x')
    Given path '/api/v2/receipts/verify'
    And request { receipt: '#(falso)', signature: '#(recibo.signature)' }
    When method post
    Then match response.valid == false
