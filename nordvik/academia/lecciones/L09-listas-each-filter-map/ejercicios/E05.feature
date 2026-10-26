# ──────────────────────────────────────────────────────────────────────
# L09 · E05 · Una condición en cada elemento
#
# El riesgo SRI va de 1 a 7. Comprueba que todos los fondos están en ese rango.
#
# Rellena cada ___ y comprueba con:  academia corregir L09
# Pista: _ es el valor de sri de cada fondo. Falta el límite superior.
# ──────────────────────────────────────────────────────────────────────
@L09 @E05
Feature: L09 E05 · Una condición en cada elemento

  Scenario: Riesgo dentro de la escala
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    And match each response.items contains { sri: '#? _ >= 1 && _ <= ___' }
