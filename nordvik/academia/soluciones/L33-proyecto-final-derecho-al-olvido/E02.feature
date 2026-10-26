# ──────────────────────────────────────────────────────────────────────
# L33 · E02 · El primer paso de la saga
#
# Completa el nombre del primer paso (triturar las claves) y si salió bien.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L33 @E02 @solucion
Feature: L33 E02 · El primer paso de la saga

  Scenario: Pasos
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    And match response.steps[0].step == 'CRYPTO_SHRED'
    And match response.steps[0].ok == true
