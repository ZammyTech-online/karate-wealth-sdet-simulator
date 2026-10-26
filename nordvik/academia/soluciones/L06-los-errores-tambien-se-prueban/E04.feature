# ──────────────────────────────────────────────────────────────────────
# L06 · E04 · Leer la pista del servidor
#
# En modo didáctico, el error trae un hint. Comprueba que te sugiere el día hábil anterior al 19 de junio.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L06 @E04 @solucion
Feature: L06 E04 · Leer la pista del servidor

  Scenario: La pista sugiere el día hábil anterior
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = '2026-06-19'
    When method get
    Then status 404
    And match response.hint contains '2026-06-18'
