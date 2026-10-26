# ──────────────────────────────────────────────────────────────────────
# L01 · E01 · La URL del catálogo
#
# Completa la URL base. No la escribas a mano: usa la variable de karate-config.js para product-catalog.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L01 @E01 @solucion
Feature: L01 E01 · La URL del catálogo

  Scenario: Leer el catálogo con la URL de la configuración
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
