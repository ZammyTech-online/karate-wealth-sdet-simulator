# ──────────────────────────────────────────────────────────────────────
# L04 · E02 · El nombre exacto del parámetro
#
# Pide páginas de 5 fondos. El parámetro del tamaño de página sigue la convención snake_case del equipo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L04 @E02 @solucion
Feature: L04 E02 · El nombre exacto del parámetro

  Scenario: Páginas de 5
    Given url catalogUrl
    And path '/api/v1/funds'
    And param page_size = 5
    When method get
    Then status 200
    And match response.page_size == 5
    And match karate.sizeOf(response.items) == 5
