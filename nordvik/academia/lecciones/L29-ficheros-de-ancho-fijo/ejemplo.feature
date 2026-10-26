# Ejemplo resuelto de L29. Ejecútalo con:  academia ejemplo L29
@ejemplo
Feature: L29 · Lote nocturno y fichero exportado

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: Exportar a las 02:30 y comprobar las 80 columnas
    * def p = call read('ejercicios/_plan.feature')
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-21T02:30:00' }
    When method post
    Then status 200
    * def olof = call read('ejercicios/_login.feature') usuarios.olof
    Given url mainframeUrl
    And path '/api/v1/batches/run'
    And headers olof.auth
    When method post
    Then status 200
    And match response.rc == '0000'
    Given path '/api/v1/batches/2026-10-21/export'
    And headers olof.auth
    When method get
    Then status 200
    # Ojo: trim() sobre el fichero entero se comería el relleno de la última línea (trampa T21)
    * def lineas = karate.filter(response.split('\n'), function(x){ return x.length > 0 })
    * def todasDe80 = function(ls){ for (var i = 0; i < ls.length; i++) if (ls[i].length != 80) return false; return true }
    * assert todasDe80(lineas)
    * match lineas[0] contains 'HD20261021'
