# Ejemplo resuelto de L14. Ejecútalo con:  academia ejemplo L14
@ejemplo
Feature: L14 · Recalcular de forma independiente

  Scenario: Mi conversión coincide con la del servidor
    Given url fxUrl
    And path '/api/v1/rates/EUR/SEK'
    And param date = '2026-10-23'
    When method get
    Then status 200
    * def tipo = parseFloat(response.rate)
    Given path '/api/v1/convert'
    And params { from: 'EUR', to: 'SEK', amount: '250.00', date: '2026-10-23' }
    When method get
    Then status 200
    * def convertir = function(importe, t){ return (importe * t).toFixed(2) }
    * match convertir(250, tipo) == response.converted
