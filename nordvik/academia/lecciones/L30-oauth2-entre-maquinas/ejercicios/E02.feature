# ──────────────────────────────────────────────────────────────────────
# L30 · E02 · Secreto equivocado
#
# Con un client_secret malo la aseguradora no te conoce. Completa estado y error.
#
# Rellena cada ___ y comprueba con:  academia corregir L30
# Pista: No sabe quién eres: 401. El error OAuth2 estándar para credenciales de cliente malas es invalid_client.
# ──────────────────────────────────────────────────────────────────────
@L30 @E02
Feature: L30 E02 · Secreto equivocado

  Scenario: Cliente desconocido
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = 'client_credentials'
    And form field client_id = 'nordvik'
    And form field client_secret = 'no-es-este'
    When method post
    Then status ___
    And match response.error == ___
