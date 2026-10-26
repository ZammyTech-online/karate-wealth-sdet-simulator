# ──────────────────────────────────────────────────────────────────────
# L14 · E02 · Recalcular una conversión
#
# Completa el número de decimales con el que redondear un importe en coronas.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L14 @E02 @solucion
Feature: L14 E02 · Recalcular una conversión

  Scenario: 100 euros en coronas
    Given url fxUrl
    And path '/api/v1/rates/EUR/SEK'
    And param date = '2026-10-23'
    When method get
    Then status 200
    * def tipo = parseFloat(response.rate)
    Given path '/api/v1/convert'
    And params { from: 'EUR', to: 'SEK', amount: '100.00', date: '2026-10-23' }
    When method get
    Then status 200
    * def convertir = function(importe, t){ return (importe * t).toFixed(2) }
    * match convertir(100, tipo) == response.converted
