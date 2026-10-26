# ──────────────────────────────────────────────────────────────────────
# L08 · E01 · Un UUID sin saber su valor
#
# Pide un UUID y comprueba que lo es, sin saber su valor.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L08 @E01 @solucion
Feature: L08 E01 · Un UUID sin saber su valor

  Scenario: Es un UUID
    Given url uuidUrl
    And path '/api/v1/uuids'
    When method get
    Then status 200
    And match response.uuids[0] == '#uuid'
