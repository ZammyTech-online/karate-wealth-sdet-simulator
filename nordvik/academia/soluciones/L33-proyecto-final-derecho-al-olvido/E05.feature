# ──────────────────────────────────────────────────────────────────────
# L33 · E05 · Dos veces no
#
# Pide el borrado del mismo cliente otra vez. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L33 @E05 @solucion
Feature: L33 E05 · Dos veces no

  Scenario: Repetido
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Primera vez' }
    When method post
    Then status 201
    Given path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Segunda vez' }
    When method post
    Then status 409
    And match response.code == 'ALREADY_ERASED'
