# Ayudante: un cliente nuevo que YA ha consentido la grabación de llamadas (VOICE_RECORDING).
# Uso:  * def c = call read('_cliente-con-grabacion.feature')
@ignore
Feature: Ayudante · cliente con consentimiento de grabación

  Scenario: Cliente con grabación
    * def c = call read('this:_cliente.feature')
    * def erik = c.erik
    * def customerId = c.customerId
    * def nationalId = c.nationalId
    Given url consentsUrl
    And path '/api/v1/consents'
    And headers erik.auth
    And request { customerId: '#(customerId)', purpose: 'VOICE_RECORDING', granted: true }
    When method post
    Then assert responseStatus == 200 || responseStatus == 201
