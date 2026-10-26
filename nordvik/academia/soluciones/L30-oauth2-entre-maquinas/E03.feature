# ──────────────────────────────────────────────────────────────────────
# L30 · E03 · Fumador con sobrepeso
#
# Un fumador (2 puntos) con IMC 31 (1 punto) sale con recargo. Completa la decisión y el recargo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L30 @E03 @solucion
Feature: L30 E03 · Fumador con sobrepeso

  Scenario: Suscripción con recargo
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = 'client_credentials'
    And form field client_id = 'nordvik'
    And form field client_secret = 'e11-insurer-client-secret-2026'
    When method post
    * def token = response.access_token
    Given path '/v1/underwriting'
    And header Authorization = 'Bearer ' + token
    And request { applicantRef: 'karate', age: 45, smoker: true, bmi: 31, conditions: [], hospitalizedLast5y: false }
    When method post
    Then status 200
    And match response.decision == 'RATED'
    And match response.loadingPct == 50
