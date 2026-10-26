# ──────────────────────────────────────────────────────────────────────
# L16 · E02 · Una clave mala
#
# Con una contraseña incorrecta el login falla. Completa el estado esperado.
#
# Rellena cada ___ y comprueba con:  academia corregir L16
# Pista: No es un 403: el servidor no sabe quién eres. Las credenciales inválidas en OAuth2 son un error del cliente.
# ──────────────────────────────────────────────────────────────────────
@L16 @E02
Feature: L16 E02 · Una clave mala

  Scenario: Clave incorrecta
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = 'cliente.ana'
    And form field password = 'no-es-esta'
    When method post
    Then status ___
