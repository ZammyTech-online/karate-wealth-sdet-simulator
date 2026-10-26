# ──────────────────────────────────────────────────────────────────────
# L04 · E03 · Varios parámetros de una vez
#
# Con un solo params, pide los fondos en dólares (USD) con riesgo mínimo 7. Hay 2.
#
# Rellena cada ___ y comprueba con:  academia corregir L04
# Pista: params recibe un objeto JSON: { nombre: valor, nombre: valor }.
# ──────────────────────────────────────────────────────────────────────
@L04 @E03
Feature: L04 E03 · Varios parámetros de una vez

  Scenario: Fondos en dólares de máximo riesgo
    Given url catalogUrl
    And path '/api/v1/funds'
    And params ___
    When method get
    Then status 200
    And match response.total == 2
