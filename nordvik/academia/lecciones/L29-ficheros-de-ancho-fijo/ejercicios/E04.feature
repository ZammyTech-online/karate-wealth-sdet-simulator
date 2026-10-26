# ──────────────────────────────────────────────────────────────────────
# L29 · E04 · Contar columnas
#
# Comprueba que la cola (última línea) dice cuántos planes hay. Completa la posición donde empieza el número (desde 0) y el total de líneas que no son de plan.
#
# Rellena cada ___ y comprueba con:  academia corregir L29
# Pista: La cola es TR + 9 dígitos: el número empieza justo después de las 2 letras. Hay una cabecera y una cola.
# ──────────────────────────────────────────────────────────────────────
@L29 @E04
Feature: L29 E04 · Contar columnas

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: La cola cuadra
    * def p = call read('_plan.feature')
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-21T03:00:00' }
    When method post
    * def olof = call read('_login.feature') usuarios.olof
    Given url mainframeUrl
    And path '/api/v1/batches/run'
    And headers olof.auth
    When method post
    Then status 200
    Given path '/api/v1/batches/2026-10-21/export'
    And headers olof.auth
    When method get
    Then status 200
    # Ojo: trim() sobre el fichero entero se comería el relleno de la última línea (trampa T21)
    * def lineas = karate.filter(response.split('\n'), function(x){ return x.length > 0 })
    * def cola = lineas[lineas.length - 1]
    * def registros = parseInt(cola.substring(___, 11), 10)
    * match registros == lineas.length - ___
