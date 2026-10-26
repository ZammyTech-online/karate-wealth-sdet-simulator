# ──────────────────────────────────────────────────────────────────────
# L29 · E03 · Dentro de la ventana
#
# Congela el reloj a las 02:30 y lanza el lote. Completa la hora y el código de retorno.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L29 @E03 @solucion
Feature: L29 E03 · Dentro de la ventana

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Lote nocturno
    * def p = call read('_plan.feature')
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-21T02:30:00' }
    When method post
    Then status 200
    * def olof = call read('_login.feature') usuarios.olof
    Given url mainframeUrl
    And path '/api/v1/batches/run'
    And headers olof.auth
    When method post
    Then status 200
    And match response.rc == '0000'
