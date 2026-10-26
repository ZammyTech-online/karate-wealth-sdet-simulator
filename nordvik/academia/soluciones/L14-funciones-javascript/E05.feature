# ──────────────────────────────────────────────────────────────────────
# L14 · E05 · Tu función como comprobación
#
# Usa la función esPositivo dentro de un fuzzy matcher para validar el importe convertido.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L14 @E05 @solucion
Feature: L14 E05 · Tu función como comprobación

  Scenario: El importe convertido es positivo
    Given url fxUrl
    And path '/api/v1/convert'
    And params { from: 'NOK', to: 'SEK', amount: '500.00' }
    When method get
    Then status 200
    * def esPositivo = function(x){ return parseFloat(x) > 0 }
    * match response.converted == '#? esPositivo(_)'
