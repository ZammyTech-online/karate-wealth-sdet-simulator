# ──────────────────────────────────────────────────────────────────────
# L09 · E02 · Filtrar por una condición
#
# Quédate con los fondos cuya comisión de gestión es menor del 0,30 %. Son 4.
#
# Rellena cada ___ y comprueba con:  academia corregir L09
# Pista: La condición compara con un número decimal escrito con punto.
# ──────────────────────────────────────────────────────────────────────
@L09 @E02
Feature: L09 E02 · Filtrar por una condición

  Scenario: Fondos baratos
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def baratos = karate.filter(response.items, function(f){ return parseFloat(f.management_fee) < ___ })
    * match baratos == '#[4]'
