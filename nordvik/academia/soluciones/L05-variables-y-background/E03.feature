# ──────────────────────────────────────────────────────────────────────
# L05 · E03 · De texto a número
#
# El NAV llega como texto. Conviértelo a número con la función JavaScript que lee decimales.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L05 @E03 @solucion
Feature: L05 E03 · De texto a número

  Scenario: Calcular con el NAV
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000002', 'latest'
    When method get
    Then status 200
    * def navNumero = parseFloat(response.nav)
    * assert typeof navNumero == 'number'
    * assert navNumero > 0
