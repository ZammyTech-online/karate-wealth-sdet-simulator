# ──────────────────────────────────────────────────────────────────────
# L21 · E01 · Sin anexo, parcial
#
# Un contrato sin anexo da una composición parcial. Completa el estado y lo que falta.
#
# Rellena cada ___ y comprueba con:  academia corregir L21
# Pista: La composición no falla: avisa. missing es una lista con el nombre de la parte ausente.
# ──────────────────────────────────────────────────────────────────────
@L21 @E01
Feature: L21 E01 · Sin anexo, parcial

  Scenario: Parcial
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.status == ___
    And match response.missing == ___
