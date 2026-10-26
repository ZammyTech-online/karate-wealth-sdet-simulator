# ──────────────────────────────────────────────────────────────────────
# L20 · E02 · Enviar a firma
#
# Envía el borrador al cliente. Completa el último trozo de la ruta y el nuevo estado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L20 @E02 @solucion
Feature: L20 E02 · Enviar a firma

  Scenario: Enviar
    * def erik = call read('_login.feature') usuarios.erik
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def contrato = response
    Given path '/api/v2/contracts', contrato.id, 'send'
    And headers erik.auth
    And request {}
    When method post
    Then status 200
    And match response.status == 'PENDING_SIGNATURE'
