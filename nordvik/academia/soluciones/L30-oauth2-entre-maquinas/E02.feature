# ──────────────────────────────────────────────────────────────────────
# L30 · E02 · Secreto equivocado
#
# Con un client_secret malo la aseguradora no te conoce. Completa estado y error.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L30 @E02 @solucion
Feature: L30 E02 · Secreto equivocado

  Scenario: Cliente desconocido
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = 'client_credentials'
    And form field client_id = 'nordvik'
    And form field client_secret = 'no-es-este'
    When method post
    Then status 401
    And match response.error == 'invalid_client'
