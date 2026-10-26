# ──────────────────────────────────────────────────────────────────────
# L16 · E04 · 401 sin token
#
# Sin cabecera Authorization, completa el estado y el código del problema.
#
# Rellena cada ___ y comprueba con:  academia corregir L16
# Pista: Sin token el servidor no sabe quién eres. El código es AUTHENTICATION_REQUIRED.
# ──────────────────────────────────────────────────────────────────────
@L16 @E04
Feature: L16 E04 · 401 sin token

  Scenario: Anónimo
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    When method get
    Then status ___
    And match response.code == ___
