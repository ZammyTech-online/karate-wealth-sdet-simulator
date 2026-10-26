# ──────────────────────────────────────────────────────────────────────
# L09 · E03 · Transformar cada elemento
#
# Construye la lista de nombres de los fondos.
#
# Rellena cada ___ y comprueba con:  academia corregir L09
# Pista: f es cada fondo; devuelve el campo que contiene su nombre.
# ──────────────────────────────────────────────────────────────────────
@L09 @E03
Feature: L09 E03 · Transformar cada elemento

  Scenario: Lista de nombres
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def nombres = karate.map(response.items, function(f){ return f.___ })
    * match nombres contains 'Nordvik Balanserad'
    * match nombres == '#[14]'
