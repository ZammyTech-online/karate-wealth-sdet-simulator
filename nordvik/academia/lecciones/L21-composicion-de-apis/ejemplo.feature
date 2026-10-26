# Ejemplo resuelto de L21. Ejecútalo con:  academia ejemplo L21
@ejemplo
Feature: L21 · contract-bundle

  Background:
    * def erik = callonce read('ejercicios/_login.feature') usuarios.erik
    * def ana = callonce read('ejercicios/_login.feature') usuarios.ana

  Scenario: Contrato + anexo = bundle completo
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'ADVISORY_MANDATE', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id

    Given path '/api/v2/contracts', id, 'annexes'
    And headers erik.auth
    And request { kind: 'FEE_SCHEDULE', content: { managementFee: '0.45', entryFee: '0.00', currency: 'SEK' } }
    When method post
    Then status 201

    Given path '/api/v2/contracts', id
    And headers ana.auth
    When method get
    Then status 200
    * def contrato = response

    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.status == 'COMPLETE'
    And match response.bundle.contract == contrato
    And match response.bundle.annex.contractId == id
    And match response.sources[*].service == ['contract-service', 'document-vault']
