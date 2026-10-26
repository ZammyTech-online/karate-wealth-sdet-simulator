# ──────────────────────────────────────────────────────────────────────
# L02 · E03 · La divisa de un fondo emergente
#
# El fondo SE0000000004 (Nordvik Emerging Markets) cotiza en dólares. Compruébalo con el campo de divisa.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L02 @E03 @solucion
Feature: L02 E03 · La divisa de un fondo emergente

  Scenario: Divisa del fondo de emergentes
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000004'
    When method get
    Then status 200
    And match response.base_currency == 'USD'
