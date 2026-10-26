# ──────────────────────────────────────────────────────────────────────
# L08 · E01 · Un UUID sin saber su valor
#
# Pide un UUID y comprueba que lo es, sin saber su valor.
#
# Rellena cada ___ y comprueba con:  academia corregir L08
# Pista: Hay un marcador con el mismo nombre que el tipo de dato.
# ──────────────────────────────────────────────────────────────────────
@L08 @E01
Feature: L08 E01 · Un UUID sin saber su valor

  Scenario: Es un UUID
    Given url uuidUrl
    And path '/api/v1/uuids'
    When method get
    Then status 200
    And match response.uuids[0] == ___
