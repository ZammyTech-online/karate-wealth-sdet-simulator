# ──────────────────────────────────────────────────────────────────────
# L02 · E03 · La divisa de un fondo emergente
#
# El fondo SE0000000004 (Nordvik Emerging Markets) cotiza en dólares. Compruébalo con el campo de divisa.
#
# Rellena cada ___ y comprueba con:  academia corregir L02
# Pista: El campo se llama base_currency (snake_case) y la divisa usa el código ISO de 3 letras.
# ──────────────────────────────────────────────────────────────────────
@L02 @E03
Feature: L02 E03 · La divisa de un fondo emergente

  Scenario: Divisa del fondo de emergentes
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000004'
    When method get
    Then status 200
    And match response.base_currency == ___
