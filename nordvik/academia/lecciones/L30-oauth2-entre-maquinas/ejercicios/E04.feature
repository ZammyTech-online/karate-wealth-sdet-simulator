# ──────────────────────────────────────────────────────────────────────
# L30 · E04 · Sin token
#
# Llama a la suscripción sin Authorization. Completa estado y error.
#
# Rellena cada ___ y comprueba con:  academia corregir L30
# Pista: Falta el token: 401. El error OAuth2 para un token ausente o malo es invalid_token.
# ──────────────────────────────────────────────────────────────────────
@L30 @E04
Feature: L30 E04 · Sin token

  Scenario: Anónimo
    Given url insurerSimUrl
    And path '/v1/underwriting'
    And request { applicantRef: 'karate', age: 40, bmi: 22 }
    When method post
    Then status ___
    And match response.error == ___
