# ──────────────────────────────────────────────────────────────────────
# L29 · E01 · El diseño de registro
#
# Pide los diseños de registro. Completa el número mínimo de tipos de línea y el tipo de la primera.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L29 @E01 @solucion
Feature: L29 E01 · El diseño de registro

  Scenario: Layouts
    Given url mainframeUrl
    And path '/api/v1/layouts'
    When method get
    Then status 200
    And assert response.length >= 4
    And match response[0].record == 'HD'
