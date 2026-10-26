# ──────────────────────────────────────────────────────────────────────
# L01 · E04 · Otro microservicio, otra URL
#
# Los precios no están en el catálogo, sino en market-data.
# Usa su URL y completa la ruta del último precio: /api/v1/prices/{isin}/latest.
#
# Rellena cada ___ y comprueba con:  academia corregir L01
# Pista: En karate-config.js la URL de market-data se llama marketUrl. El último trozo de la ruta es un texto.
# ──────────────────────────────────────────────────────────────────────
@L01 @E04
Feature: L01 E04 · Otro microservicio, otra URL

  Scenario: Último precio publicado
    Given url ___
    And path '/api/v1/prices', 'SE0000000001', ___
    When method get
    Then status 200
    And match response.isin == 'SE0000000001'
