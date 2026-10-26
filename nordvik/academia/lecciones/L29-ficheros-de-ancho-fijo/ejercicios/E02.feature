# ──────────────────────────────────────────────────────────────────────
# L29 · E02 · Fuera de la ventana de lote
#
# A las 10:00 el lote no puede correr. Completa el estado y el código de retorno.
#
# Rellena cada ___ y comprueba con:  academia corregir L29
# Pista: Es un conflicto con el estado del sistema. El rc de la ventana cerrada está en la tabla del README.
# ──────────────────────────────────────────────────────────────────────
@L29 @E02
Feature: L29 E02 · Fuera de la ventana de lote

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Ventana cerrada
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-21T10:00:00' }
    When method post
    * def olof = call read('_login.feature') usuarios.olof
    Given url mainframeUrl
    And path '/api/v1/batches/run'
    And headers olof.auth
    When method post
    Then status ___
    And match response.rc == ___
