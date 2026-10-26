# ──────────────────────────────────────────────────────────────────────
# L03 · E04 · Un objeto dentro de una lista
#
# El fondo SE0000000003 (Nordic Small Cap) cobra un 1.00 % de entrada.
# Comprueba que su lista de comisiones contiene ese objeto.
#
# Rellena cada ___ y comprueba con:  academia corregir L03
# Pista: Escribe un objeto JSON con los dos campos de una comisión: type y percent.
# ──────────────────────────────────────────────────────────────────────
@L03 @E04
Feature: L03 E04 · Un objeto dentro de una lista

  Scenario: Comisión de entrada del fondo de pequeñas compañías
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000003'
    When method get
    Then status 200
    And match response.fees contains ___
