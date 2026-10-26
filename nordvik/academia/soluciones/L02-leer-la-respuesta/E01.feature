# ──────────────────────────────────────────────────────────────────────
# L02 · E01 · El nombre de un fondo
#
# El fondo SE0000000002 se llama 'Nordvik Sverige Index'. Compruébalo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L02 @E01 @solucion
Feature: L02 E01 · El nombre de un fondo

  Scenario: Nombre del fondo índice sueco
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000002'
    When method get
    Then status 200
    And match response.name == 'Nordvik Sverige Index'
