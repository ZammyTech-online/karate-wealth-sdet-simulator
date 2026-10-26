# ──────────────────────────────────────────────────────────────────────
# L20 · E01 · Nace un borrador
#
# Erik crea un contrato marco para Ana. Completa el tipo y el estado inicial.
#
# Rellena cada ___ y comprueba con:  academia corregir L20
# Pista: El contrato marco es MASTER_AGREEMENT. Todo contrato nace como borrador.
# ──────────────────────────────────────────────────────────────────────
@L20 @E01
Feature: L20 E01 · Nace un borrador

  Scenario: Crear
    * def erik = call read('_login.feature') usuarios.erik
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: ___, customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    And match response.status == ___
    And match response.contentHash == '#regex [0-9a-f]{64}'
