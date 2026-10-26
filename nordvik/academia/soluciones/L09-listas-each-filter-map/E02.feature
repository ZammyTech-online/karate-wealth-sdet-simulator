# ──────────────────────────────────────────────────────────────────────
# L09 · E02 · Filtrar por una condición
#
# Quédate con los fondos cuya comisión de gestión es menor del 0,30 %. Son 4.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L09 @E02 @solucion
Feature: L09 E02 · Filtrar por una condición

  Scenario: Fondos baratos
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def baratos = karate.filter(response.items, function(f){ return parseFloat(f.management_fee) < 0.30 })
    * match baratos == '#[4]'
