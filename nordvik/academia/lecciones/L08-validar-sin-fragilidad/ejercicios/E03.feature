# ──────────────────────────────────────────────────────────────────────
# L08 · E03 · El tamaño de una lista
#
# Pedimos 3 UUID. Comprueba que la lista tiene exactamente 3 elementos con un solo marcador.
#
# Rellena cada ___ y comprueba con:  academia corregir L08
# Pista: El marcador de tamaño usa corchetes con el número dentro.
# ──────────────────────────────────────────────────────────────────────
@L08 @E03
Feature: L08 E03 · El tamaño de una lista

  Scenario: Tres elementos
    Given url uuidUrl
    And path '/api/v1/uuids'
    And param count = 3
    When method get
    Then status 200
    And match response.uuids == ___
