# ──────────────────────────────────────────────────────────────────────
# L09 · E06 · Contar los de una divisa
#
# ¿Cuántos fondos cotizan en euros? Filtra y comprueba que son 2.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L09 @E06 @solucion
Feature: L09 E06 · Contar los de una divisa

  Scenario: Fondos en euros
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def enEuros = karate.filter(response.items, function(f){ return f.base_currency == 'EUR' })
    * match enEuros == '#[2]'
