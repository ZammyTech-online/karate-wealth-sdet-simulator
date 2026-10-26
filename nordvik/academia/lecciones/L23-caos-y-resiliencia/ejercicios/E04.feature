# ──────────────────────────────────────────────────────────────────────
# L23 · E04 · Reintenta hasta que pase
#
# Una regla tumba /api/v1/uuids dos veces. Completa la condición de reintento para llegar al 200.
#
# Rellena cada ___ y comprueba con:  academia corregir L23
# Pista: retry until evalúa una expresión con el estado de la respuesta (no response.status: aquí el cuerpo no tiene status).
# ──────────────────────────────────────────────────────────────────────
@L23 @E04
Feature: L23 E04 · Reintenta hasta que pase

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: Dos fallos y a la tercera
    Given url chaosUrl
    And path '/api/v1/faults'
    And request { service: 'uuid-service', pathPrefix: '/api/v1/uuids', status: 503, times: 2 }
    When method post
    Then status 201
    * configure retry = { count: 5, interval: 100 }
    Given url uuidUrl
    And path '/api/v1/uuids'
    And retry until ___
    When method get
    Then status 200
