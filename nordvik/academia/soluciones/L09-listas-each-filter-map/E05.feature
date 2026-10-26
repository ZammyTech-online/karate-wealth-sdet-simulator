# ──────────────────────────────────────────────────────────────────────
# L09 · E05 · Una condición en cada elemento
#
# El riesgo SRI va de 1 a 7. Comprueba que todos los fondos están en ese rango.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L09 @E05 @solucion
Feature: L09 E05 · Una condición en cada elemento

  Scenario: Riesgo dentro de la escala
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    And match each response.items contains { sri: '#? _ >= 1 && _ <= 7' }
