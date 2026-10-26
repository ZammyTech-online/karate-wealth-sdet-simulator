# ──────────────────────────────────────────────────────────────────────
# L00 · E02 · Predice el código de éxito
#
# Pedimos la lista de fondos, que existe y está bien pedida. ¿Qué código devolverá el servidor?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L00 @E02 @solucion
Feature: L00 E02 · Predice el código de éxito

  Scenario: El catálogo responde bien
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
