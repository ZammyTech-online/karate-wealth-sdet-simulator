# ──────────────────────────────────────────────────────────────────────
# L09 · E01 · Cada elemento cumple
#
# Filtramos por EQUITY. Comprueba que cada fondo devuelto es de esa categoría.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L09 @E01 @solucion
Feature: L09 E01 · Cada elemento cumple

  Scenario: El filtro de categoría funciona
    Given url catalogUrl
    And path '/api/v1/funds'
    And param category = 'EQUITY'
    When method get
    Then status 200
    And match each response.items contains { category: 'EQUITY' }
