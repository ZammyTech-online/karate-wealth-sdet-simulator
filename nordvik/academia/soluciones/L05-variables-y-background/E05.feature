# ──────────────────────────────────────────────────────────────────────
# L05 · E05 · Construir un valor con una expresión
#
# Construye la fecha 2026-03-02 uniendo tres trozos de texto. Falta el del mes.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L05 @E05 @solucion
Feature: L05 E05 · Construir un valor con una expresión

  Scenario: Precio del 2 de marzo
    * def fecha = '2026-' + '03' + '-02'
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = fecha
    When method get
    Then status 200
    And match response.date == '2026-03-02'
