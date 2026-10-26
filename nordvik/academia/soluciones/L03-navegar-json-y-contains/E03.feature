# ──────────────────────────────────────────────────────────────────────
# L03 · E03 · Comprobar solo una parte
#
# Solo nos importan dos campos del fondo: que sea de renta variable (EQUITY) y de riesgo 4.
# Usa la variante de match que permite que sobren campos.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L03 @E03 @solucion
Feature: L03 E03 · Comprobar solo una parte

  Scenario: Categoría y riesgo del fondo global
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    And match response contains { category: 'EQUITY', sri: 4 }
