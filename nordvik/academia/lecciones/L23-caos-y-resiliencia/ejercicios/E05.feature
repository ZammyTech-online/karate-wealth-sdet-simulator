# ──────────────────────────────────────────────────────────────────────
# L23 · E05 · ¿Tu caos o un fallo real?
#
# El fallo inyectado por una regla lleva su id en la cabecera X-Chaos-Injected. Completa el nombre de la cabecera y con qué se compara.
#
# Rellena cada ___ y comprueba con:  academia corregir L23
# Pista: karate.response.header('...') no distingue mayúsculas. El id es el de la regla que creaste.
# ──────────────────────────────────────────────────────────────────────
@L23 @E05
Feature: L23 E05 · ¿Tu caos o un fallo real?

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: Rastrear el fallo
    Given url chaosUrl
    And path '/api/v1/faults'
    And request { service: 'uuid-service', pathPrefix: '/api/v1/uuids', status: 503, times: 1 }
    When method post
    Then status 201
    * def regla = response.id
    Given url uuidUrl
    And path '/api/v1/uuids'
    When method get
    Then status 503
    And match karate.response.header(___) == ___
