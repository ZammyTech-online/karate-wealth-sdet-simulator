# ──────────────────────────────────────────────────────────────────────
# L08 · E03 · El tamaño de una lista
#
# Pedimos 3 UUID. Comprueba que la lista tiene exactamente 3 elementos con un solo marcador.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L08 @E03 @solucion
Feature: L08 E03 · El tamaño de una lista

  Scenario: Tres elementos
    Given url uuidUrl
    And path '/api/v1/uuids'
    And param count = 3
    When method get
    Then status 200
    And match response.uuids == '#[3]'
