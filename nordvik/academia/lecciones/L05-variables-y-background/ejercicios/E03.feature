# ──────────────────────────────────────────────────────────────────────
# L05 · E03 · De texto a número
#
# El NAV llega como texto. Conviértelo a número con la función JavaScript que lee decimales.
#
# Rellena cada ___ y comprueba con:  academia corregir L05
# Pista: En JavaScript hay parseInt (enteros) y otra para números con decimales.
# ──────────────────────────────────────────────────────────────────────
@L05 @E03
Feature: L05 E03 · De texto a número

  Scenario: Calcular con el NAV
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000002', 'latest'
    When method get
    Then status 200
    * def navNumero = ___
    * assert typeof navNumero == 'number'
    * assert navNumero > 0
