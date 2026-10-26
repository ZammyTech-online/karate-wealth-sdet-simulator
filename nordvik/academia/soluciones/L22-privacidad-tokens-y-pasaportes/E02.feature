# ──────────────────────────────────────────────────────────────────────
# L22 · E02 · Lo que NO tiene que salir
#
# La respuesta trae la máscara y nunca el número completo. Completa la máscara y el operador que lo comprueba.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L22 @E02 @solucion
Feature: L22 E02 · Lo que NO tiene que salir

  Scenario: Máscara y nada en claro
    * def c = call read('_cliente.feature')
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    And match response.masked contains '**-****'
    * def texto = karate.toString(response)
    * match texto !contains c.nationalId
