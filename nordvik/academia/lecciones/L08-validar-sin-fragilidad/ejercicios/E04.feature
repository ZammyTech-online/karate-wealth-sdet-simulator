# ──────────────────────────────────────────────────────────────────────
# L08 · E04 · Todos los elementos
#
# Pedimos 50 UUID. Comprueba con match each que todos son UUID.
#
# Rellena cada ___ y comprueba con:  academia corregir L08
# Pista: match each aplica la comprobación a cada elemento de la lista.
# ──────────────────────────────────────────────────────────────────────
@L08 @E04
Feature: L08 E04 · Todos los elementos

  Scenario: Cincuenta UUID válidos
    Given url uuidUrl
    And path '/api/v1/uuids'
    And param count = 50
    When method get
    Then status 200
    And match each response.uuids == ___
