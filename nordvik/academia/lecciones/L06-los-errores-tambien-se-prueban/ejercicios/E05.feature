# ──────────────────────────────────────────────────────────────────────
# L06 · E05 · Coma decimal: un error clásico
#
# En España escribimos 100,00, pero la API solo admite punto decimal. Predice el código.
#
# Rellena cada ___ y comprueba con:  academia corregir L06
# Pista: El servidor no puede entender el importe: la petición es inválida.
# ──────────────────────────────────────────────────────────────────────
@L06 @E05
Feature: L06 E05 · Coma decimal: un error clásico

  Scenario: Importe con coma
    Given url fxUrl
    And path '/api/v1/convert'
    And params { from: 'EUR', to: 'SEK', amount: '100,00' }
    When method get
    Then status ___
    And match response.code == 'INVALID_AMOUNT'
