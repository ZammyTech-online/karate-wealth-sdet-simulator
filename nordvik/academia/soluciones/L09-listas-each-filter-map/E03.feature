# ──────────────────────────────────────────────────────────────────────
# L09 · E03 · Transformar cada elemento
#
# Construye la lista de nombres de los fondos.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L09 @E03 @solucion
Feature: L09 E03 · Transformar cada elemento

  Scenario: Lista de nombres
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def nombres = karate.map(response.items, function(f){ return f.name })
    * match nombres contains 'Nordvik Balanserad'
    * match nombres == '#[14]'
