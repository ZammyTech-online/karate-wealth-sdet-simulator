# ──────────────────────────────────────────────────────────────────────
# L00 · E04 · Predice el código de algo que no existe
#
# Pedimos el fondo SE9999999999. Su formato es correcto, pero no está en el catálogo. ¿Qué código devolverá?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L00 @E04 @solucion
Feature: L00 E04 · Predice el código de algo que no existe

  Scenario: Un fondo que no existe
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    When method get
    Then status 404
