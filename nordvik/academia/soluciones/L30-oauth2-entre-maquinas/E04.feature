# ──────────────────────────────────────────────────────────────────────
# L30 · E04 · Sin token
#
# Llama a la suscripción sin Authorization. Completa estado y error.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L30 @E04 @solucion
Feature: L30 E04 · Sin token

  Scenario: Anónimo
    Given url insurerSimUrl
    And path '/v1/underwriting'
    And request { applicantRef: 'karate', age: 40, bmi: 22 }
    When method post
    Then status 401
    And match response.error == 'invalid_token'
