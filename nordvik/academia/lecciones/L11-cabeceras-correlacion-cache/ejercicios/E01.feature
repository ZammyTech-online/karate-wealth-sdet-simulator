# ──────────────────────────────────────────────────────────────────────
# L11 · E01 · Envía tu correlationId
#
# Envía el correlationId 'mi-primer-id' y comprueba que vuelve en la respuesta.
#
# Rellena cada ___ y comprueba con:  academia corregir L11
# Pista: El valor de una cabecera es un texto.
# ──────────────────────────────────────────────────────────────────────
@L11 @E01
Feature: L11 E01 · Envía tu correlationId

  Scenario: Mi identificador viaja de ida y vuelta
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    And header X-Correlation-Id = ___
    When method get
    Then status 200
    And match responseHeaders['X-Correlation-Id'][0] == 'mi-primer-id'
