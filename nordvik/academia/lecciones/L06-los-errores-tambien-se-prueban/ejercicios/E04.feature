# ──────────────────────────────────────────────────────────────────────
# L06 · E04 · Leer la pista del servidor
#
# En modo didáctico, el error trae un hint. Comprueba que te sugiere el día hábil anterior al 19 de junio.
#
# Rellena cada ___ y comprueba con:  academia corregir L06
# Pista: El 19 de junio de 2026 es viernes; el día hábil anterior es el día antes.
# ──────────────────────────────────────────────────────────────────────
@L06 @E04
Feature: L06 E04 · Leer la pista del servidor

  Scenario: La pista sugiere el día hábil anterior
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = '2026-06-19'
    When method get
    Then status 404
    And match response.hint contains ___
