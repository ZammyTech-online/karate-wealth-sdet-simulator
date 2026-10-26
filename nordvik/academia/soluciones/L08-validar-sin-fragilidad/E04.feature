# ──────────────────────────────────────────────────────────────────────
# L08 · E04 · Todos los elementos
#
# Pedimos 50 UUID. Comprueba con match each que todos son UUID.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L08 @E04 @solucion
Feature: L08 E04 · Todos los elementos

  Scenario: Cincuenta UUID válidos
    Given url uuidUrl
    And path '/api/v1/uuids'
    And param count = 50
    When method get
    Then status 200
    And match each response.uuids == '#uuid'
