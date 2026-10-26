# ──────────────────────────────────────────────────────────────────────
# L31 · E01 · Un esquema en snake_case
#
# Completa el nombre del campo de moneda base tal como lo escribe product-catalog y el marcador del número de riesgo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L31 @E01 @solucion
Feature: L31 E01 · Un esquema en snake_case

  Scenario: Esquema del fondo
    * def fondo = { isin: '#string', name: '#string', base_currency: '#string', sri: '#number' }
    Given url catalogUrl
    And path '/api/v1/funds/SE0000000002'
    When method get
    Then status 200
    And match response contains fondo
