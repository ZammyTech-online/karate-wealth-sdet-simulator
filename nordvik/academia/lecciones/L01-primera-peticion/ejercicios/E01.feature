# ──────────────────────────────────────────────────────────────────────
# L01 · E01 · La URL del catálogo
#
# Completa la URL base. No la escribas a mano: usa la variable de karate-config.js para product-catalog.
#
# Rellena cada ___ y comprueba con:  academia corregir L01
# Pista: Abre config/karate-config.js: hay una variable por servicio. La del catálogo termina en Url.
# ──────────────────────────────────────────────────────────────────────
@L01 @E01
Feature: L01 E01 · La URL del catálogo

  Scenario: Leer el catálogo con la URL de la configuración
    Given url ___
    And path '/api/v1/funds'
    When method get
    Then status 200
