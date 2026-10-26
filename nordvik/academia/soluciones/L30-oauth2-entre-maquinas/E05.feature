# ──────────────────────────────────────────────────────────────────────
# L30 · E05 · El token caducado
#
# Invalida todos los tokens y repite la llamada con el viejo; luego pide otro. Completa el estado con el token viejo y con el nuevo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L30 @E05 @solucion
Feature: L30 E05 · El token caducado

  Scenario: Renovar
    * def pedir = function(){ var r = karate.http(insurerSimUrl + '/oauth/token').header('Content-Type', 'application/x-www-form-urlencoded').post('grant_type=client_credentials&client_id=nordvik&client_secret=e11-insurer-client-secret-2026'); return r.body.access_token }
    * def viejo = pedir()
    Given url insurerSimUrl
    And path '/__admin/tokens/expire-all'
    When method post
    Then status 200
    Given path '/v1/underwriting'
    And header Authorization = 'Bearer ' + viejo
    And request { applicantRef: 'karate', age: 40, smoker: false, bmi: 23, conditions: [], hospitalizedLast5y: false }
    When method post
    Then status 401
    * def nuevo = pedir()
    Given path '/v1/underwriting'
    And header Authorization = 'Bearer ' + nuevo
    And request { applicantRef: 'karate', age: 40, smoker: false, bmi: 23, conditions: [], hospitalizedLast5y: false }
    When method post
    Then status 200
