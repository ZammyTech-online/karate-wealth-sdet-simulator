# ──────────────────────────────────────────────────────────────────────
# L16 · E02 · Una clave mala
#
# Con una contraseña incorrecta el login falla. Completa el estado esperado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L16 @E02 @solucion
Feature: L16 E02 · Una clave mala

  Scenario: Clave incorrecta
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = 'cliente.ana'
    And form field password = 'no-es-esta'
    When method post
    Then status 400
