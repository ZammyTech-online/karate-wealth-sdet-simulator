# ──────────────────────────────────────────────────────────────────────
# L09 · E06 · Contar los de una divisa
#
# ¿Cuántos fondos cotizan en euros? Filtra y comprueba que son 2.
#
# Rellena cada ___ y comprueba con:  academia corregir L09
# Pista: Compara el campo base_currency con el código ISO del euro.
# ──────────────────────────────────────────────────────────────────────
@L09 @E06
Feature: L09 E06 · Contar los de una divisa

  Scenario: Fondos en euros
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def enEuros = karate.filter(response.items, function(f){ return f.base_currency == ___ })
    * match enEuros == '#[2]'
