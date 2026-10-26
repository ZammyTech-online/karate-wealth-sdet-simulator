# ──────────────────────────────────────────────────────────────────────
# L01 · E04 · Otro microservicio, otra URL
#
# Los precios no están en el catálogo, sino en market-data.
# Usa su URL y completa la ruta del último precio: /api/v1/prices/{isin}/latest.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L01 @E04 @solucion
Feature: L01 E04 · Otro microservicio, otra URL

  Scenario: Último precio publicado
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    And match response.isin == 'SE0000000001'
