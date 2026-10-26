# ──────────────────────────────────────────────────────────────────────
# L15 · E01 · Suite de regresión del mercado
#
# Completa los 6 escenarios de la suite. Lee el README de L15 antes de empezar.
#
# Rellena cada ___ y comprueba con:  academia corregir L15
# Pista: Trabaja un escenario cada vez. Los huecos grandes son líneas completas que ya has escrito en otras lecciones.
# ──────────────────────────────────────────────────────────────────────
@L15 @E01
Feature: L15 E01 · Suite de regresión del mercado

  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }

  Scenario: El catálogo tiene 14 fondos con forma válida
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    And match response.total == ___
    And match each response.items == ___

  Scenario: Cada fondo abierto tiene precio y su divisa coincide
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def abiertos = karate.filter(response.items, function(f){ return f.status == ___ })
    * match abiertos == '#[13]'
    * def comprobar =
      """
      function(f) {
        var r = karate.http(marketUrl + '/api/v1/prices/' + f.isin + '/latest').get();
        return r.status == 200 && r.body.currency == f.___;
      }
      """
    * def resultados = karate.map(abiertos, comprobar)
    * match each resultados == true

  Scenario Outline: <nombre> no tiene precio
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000002'
    And param date = '<fecha>'
    When method get
    Then status ___
    And match response.code == 'NO_PRICE_FOR_DATE'

    Examples:
      | fecha      | nombre              |
      | 2026-01-06 | Epifanía            |
      | 2026-06-19 | Víspera de San Juan |
      | 2026-05-14 | Ascensión           |

  Scenario: La conversión cuadra con el tipo publicado
    Given url fxUrl
    And path '/api/v1/rates/USD/SEK'
    And param date = '2026-10-23'
    When method get
    Then status 200
    * def tipo = parseFloat(response.rate)
    Given path '/api/v1/convert'
    And params { from: 'USD', to: 'SEK', amount: '1000.00', date: '2026-10-23' }
    When method get
    Then status 200
    And match response.converted == ___

  Scenario: La hora de corte se respeta
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T14:59:00+01:00' }
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == true
    Given url clockUrl
    And path '/api/v1/clock/advance'
    And request ___
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == ___

  Scenario: Los errores siguen el formato RFC 9457
    Given url fxUrl
    And path '/api/v1/rates/EUR/XXX'
    When method get
    Then status 400
    And match responseHeaders['Content-Type'][0] contains 'application/problem+json'
    And match response contains ___
