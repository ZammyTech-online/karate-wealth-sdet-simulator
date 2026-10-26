# ──────────────────────────────────────────────────────────────────────
# L00 · E03 · Pide un fondo concreto
#
# Pide el detalle del fondo 'Nordvik Global Equity'.
# Su ISIN lo encontrarás en Swagger (GET /api/v1/funds) o en el ejemplo de la teoría.
#
# Rellena cada ___ y comprueba con:  academia corregir L00
# Pista: El ISIN del fondo va al final de la ruta, después de /api/v1/funds/.
# ──────────────────────────────────────────────────────────────────────
@L00 @E03
Feature: L00 E03 · Pide un fondo concreto

  Scenario: Detalle de Nordvik Global Equity
    Given url catalogUrl
    And path '/api/v1/funds/___'
    When method get
    Then status 200
    And match response.name == 'Nordvik Global Equity'
