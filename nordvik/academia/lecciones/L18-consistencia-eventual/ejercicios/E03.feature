# ──────────────────────────────────────────────────────────────────────
# L18 · E03 · Recién creado, todavía no
#
# Justo al abrir el ingreso, antes de transferir nada, ¿en qué estado está?
#
# Rellena cada ___ y comprueba con:  academia corregir L18
# Pista: Nordvik está esperando a que llegue el dinero. Mira el ejemplo de la lección.
# ──────────────────────────────────────────────────────────────────────
@L18 @E03
Feature: L18 E03 · Recién creado, todavía no

  Scenario: Estado inicial
    * def olof = call read('_login.feature') usuarios.olof
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '75.00' }
    When method post
    Then status 201
    And match response.status == ___
    And match response.reference == '#regex [0-9]+'
