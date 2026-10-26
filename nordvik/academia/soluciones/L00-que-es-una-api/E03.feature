# ──────────────────────────────────────────────────────────────────────
# L00 · E03 · Pide un fondo concreto
#
# Pide el detalle del fondo 'Nordvik Global Equity'.
# Su ISIN lo encontrarás en Swagger (GET /api/v1/funds) o en el ejemplo de la teoría.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L00 @E03 @solucion
Feature: L00 E03 · Pide un fondo concreto

  Scenario: Detalle de Nordvik Global Equity
    Given url catalogUrl
    And path '/api/v1/funds/SE0000000001'
    When method get
    Then status 200
    And match response.name == 'Nordvik Global Equity'
