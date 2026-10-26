# ──────────────────────────────────────────────────────────────────────
# L18 · E03 · Recién creado, todavía no
#
# Justo al abrir el ingreso, antes de transferir nada, ¿en qué estado está?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L18 @E03 @solucion
Feature: L18 E03 · Recién creado, todavía no

  Scenario: Estado inicial
    * def olof = call read('_login.feature') usuarios.olof
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '75.00' }
    When method post
    Then status 201
    And match response.status == 'AWAITING_FUNDS'
    And match response.reference == '#regex [0-9]+'
