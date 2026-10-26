# ──────────────────────────────────────────────────────────────────────
# L08 · E05 · Una expresión regular
#
# Un UUID v7 tiene un 7 al principio del tercer bloque. Completa la expresión regular.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L08 @E05 @solucion
Feature: L08 E05 · Una expresión regular

  Scenario: Es un UUID de versión 7
    Given url uuidUrl
    And path '/api/v1/uuids/one'
    And param version = 7
    When method get
    Then status 200
    And match response.uuid == '#regex [0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}'
