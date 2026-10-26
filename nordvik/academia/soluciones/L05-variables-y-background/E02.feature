# ──────────────────────────────────────────────────────────────────────
# L05 · E02 · Guardar un campo de la respuesta
#
# Guarda el NAV del último precio en la variable nav.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L05 @E02 @solucion
Feature: L05 E02 · Guardar un campo de la respuesta

  Scenario: El NAV es un texto con decimales
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    * def nav = response.nav
    * assert nav.length > 4
    * match nav contains '.'
