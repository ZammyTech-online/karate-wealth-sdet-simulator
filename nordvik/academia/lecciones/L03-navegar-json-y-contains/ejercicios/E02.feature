# ──────────────────────────────────────────────────────────────────────
# L03 · E02 · El tercer elemento
#
# La tercera comisión (fees[2]) es la de salida. ¿Cómo se llama su tipo? Míralo en Swagger.
#
# Rellena cada ___ y comprueba con:  academia corregir L03
# Pista: Las tres comisiones son gestión, entrada y salida, en inglés y en mayúsculas.
# ──────────────────────────────────────────────────────────────────────
@L03 @E02
Feature: L03 E02 · El tercer elemento

  Scenario: La tercera comisión es la de salida
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    And match response.fees[2].type == ___
