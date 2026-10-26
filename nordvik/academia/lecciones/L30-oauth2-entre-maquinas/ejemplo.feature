# Ejemplo resuelto de L30. Ejecútalo con:  academia ejemplo L30
@ejemplo
Feature: L30 · Token de máquina y suscripción

  Scenario: Token y suscripción de una persona sana
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = 'client_credentials'
    And form field client_id = 'nordvik'
    And form field client_secret = 'e11-insurer-client-secret-2026'
    When method post
    Then status 200
    * def token = response.access_token
    Given path '/v1/underwriting'
    And header Authorization = 'Bearer ' + token
    And request { applicantRef: 'karate', age: 40, smoker: false, bmi: 23, conditions: [], hospitalizedLast5y: false }
    When method post
    Then status 200
    And match response contains { decision: 'STANDARD', loadingPct: 0, answersRetained: false }
