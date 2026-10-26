# ──────────────────────────────────────────────────────────────────────
# L16 · E04 · 401 sin token
#
# Sin cabecera Authorization, completa el estado y el código del problema.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L16 @E04 @solucion
Feature: L16 E04 · 401 sin token

  Scenario: Anónimo
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    When method get
    Then status 401
    And match response.code == 'AUTHENTICATION_REQUIRED'
