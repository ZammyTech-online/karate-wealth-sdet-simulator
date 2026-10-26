# ──────────────────────────────────────────────────────────────────────
# L08 · E05 · Una expresión regular
#
# Un UUID v7 tiene un 7 al principio del tercer bloque. Completa la expresión regular.
#
# Rellena cada ___ y comprueba con:  academia corregir L08
# Pista: El tercer bloque empieza por la versión del UUID.
# ──────────────────────────────────────────────────────────────────────
@L08 @E05
Feature: L08 E05 · Una expresión regular

  Scenario: Es un UUID de versión 7
    Given url uuidUrl
    And path '/api/v1/uuids/one'
    And param version = 7
    When method get
    Then status 200
    And match response.uuid == '#regex [0-9a-f]{8}-[0-9a-f]{4}-___[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}'
