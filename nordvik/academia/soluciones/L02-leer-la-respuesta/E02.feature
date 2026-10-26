# ──────────────────────────────────────────────────────────────────────
# L02 · E02 · Un número no es un texto
#
# Comprueba que el riesgo (sri) del fondo SE0000000002 es 5. Ojo: en el JSON es un número.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L02 @E02 @solucion
Feature: L02 E02 · Un número no es un texto

  Scenario: Riesgo del fondo índice sueco
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000002'
    When method get
    Then status 200
    And match response.sri == 5
