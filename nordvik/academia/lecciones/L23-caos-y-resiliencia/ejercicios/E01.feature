# ──────────────────────────────────────────────────────────────────────
# L23 · E01 · Un fallo de una sola petición
#
# Pide /health de uuid-service con un 500 inyectado. Completa la cabecera y el código del problema.
#
# Rellena cada ___ y comprueba con:  academia corregir L23
# Pista: La cabecera es X-Chaos-... (mira el README). El código dice que el fallo es inyectado.
# ──────────────────────────────────────────────────────────────────────
@L23 @E01
Feature: L23 E01 · Un fallo de una sola petición

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: 500 a propósito
    Given url uuidUrl
    And path '/health'
    And header ___ = 'status=500'
    When method get
    Then status 500
    And match response.code == ___
