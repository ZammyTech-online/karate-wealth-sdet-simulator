# ──────────────────────────────────────────────────────────────────────
# L06 · E01 · Un festivo no tiene precio
#
# Pide el NAV del 19 de junio de 2026 (Víspera de San Juan, festivo en Suecia).
# Predice el código HTTP y el code del error.
#
# Rellena cada ___ y comprueba con:  academia corregir L06
# Pista: El recurso 'precio de ese día' no existe. El code lo dice en inglés: no hay precio para la fecha.
# ──────────────────────────────────────────────────────────────────────
@L06 @E01
Feature: L06 E01 · Un festivo no tiene precio

  Scenario: Sin precio en la Víspera de San Juan
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = '2026-06-19'
    When method get
    Then status ___
    And match response.code == ___
