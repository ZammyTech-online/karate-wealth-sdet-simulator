# ──────────────────────────────────────────────────────────────────────
# L06 · E01 · Un festivo no tiene precio
#
# Pide el NAV del 19 de junio de 2026 (Víspera de San Juan, festivo en Suecia).
# Predice el código HTTP y el code del error.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L06 @E01 @solucion
Feature: L06 E01 · Un festivo no tiene precio

  Scenario: Sin precio en la Víspera de San Juan
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = '2026-06-19'
    When method get
    Then status 404
    And match response.code == 'NO_PRICE_FOR_DATE'
