# ──────────────────────────────────────────────────────────────────────
# L01 · E02 · La ruta de la lista de fondos
#
# Completa la ruta para leer la lista de fondos.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L01 @E02 @solucion
Feature: L01 E02 · La ruta de la lista de fondos

  Scenario: Lista de fondos
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
