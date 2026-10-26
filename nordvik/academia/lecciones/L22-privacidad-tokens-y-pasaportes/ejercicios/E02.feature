# ──────────────────────────────────────────────────────────────────────
# L22 · E02 · Lo que NO tiene que salir
#
# La respuesta trae la máscara y nunca el número completo. Completa la máscara y el operador que lo comprueba.
#
# Rellena cada ___ y comprueba con:  academia corregir L22
# Pista: La máscara oculta los últimos dígitos así: 19850312-****. Para 'no contiene' Karate usa la negación de contains.
# ──────────────────────────────────────────────────────────────────────
@L22 @E02
Feature: L22 E02 · Lo que NO tiene que salir

  Scenario: Máscara y nada en claro
    * def c = call read('_cliente.feature')
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    And match response.masked contains ___
    * def texto = karate.toString(response)
    * match texto ___ c.nationalId
