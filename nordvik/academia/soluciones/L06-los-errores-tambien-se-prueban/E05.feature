# ──────────────────────────────────────────────────────────────────────
# L06 · E05 · Coma decimal: un error clásico
#
# En España escribimos 100,00, pero la API solo admite punto decimal. Predice el código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L06 @E05 @solucion
Feature: L06 E05 · Coma decimal: un error clásico

  Scenario: Importe con coma
    Given url fxUrl
    And path '/api/v1/convert'
    And params { from: 'EUR', to: 'SEK', amount: '100,00' }
    When method get
    Then status 400
    And match response.code == 'INVALID_AMOUNT'
