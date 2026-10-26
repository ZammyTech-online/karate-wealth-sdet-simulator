# ──────────────────────────────────────────────────────────────────────
# L29 · E01 · El diseño de registro
#
# Pide los diseños de registro. Completa el número mínimo de tipos de línea y el tipo de la primera.
#
# Rellena cada ___ y comprueba con:  academia corregir L29
# Pista: Hay cabecera, plan, cola y saldo: cuatro. La primera es la cabecera (HD).
# ──────────────────────────────────────────────────────────────────────
@L29 @E01
Feature: L29 E01 · El diseño de registro

  Scenario: Layouts
    Given url mainframeUrl
    And path '/api/v1/layouts'
    When method get
    Then status 200
    And assert response.length >= ___
    And match response[0].record == ___
