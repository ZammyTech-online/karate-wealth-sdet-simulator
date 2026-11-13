@pedagogico @mid @fx
Feature: M-D: Manipulación de Tasas de Cambio FX con JavaScript Embebido (GraalVM)
  # [CONTEXTO ACTUARIAL]: Para liquidar una orden internacional SEK/EUR, Karate debe
  # calcular en memoria si la conversión aplicada coincide con el spread actuarial pactado.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Filtrar tasas activas y calcular contravalor en SEK con función JS
    Given path '/api/v1/fx/rates'
    When method get
    Then status 200
    # Función JS pura ejecutada en el motor GraalVM de Karate
    * def filterEur = function(x){ return x.pair == 'EURSEK' }
    * def rate = karate.filter(response.rates, filterEur)[0].midRate
    * def amountEur = 1000
    * def expectedSek = amountEur * rate
    * match expectedSek > 10000
