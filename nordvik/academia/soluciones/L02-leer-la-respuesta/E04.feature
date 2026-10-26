# ──────────────────────────────────────────────────────────────────────
# L02 · E04 · Guarda la respuesta en una variable
#
# Guarda la respuesta completa en la variable fondo y comprueba con ella que el fondo está abierto.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L02 @E04 @solucion
Feature: L02 E04 · Guarda la respuesta en una variable

  Scenario: Tech Growth está abierto a inversiones
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000006'
    When method get
    Then status 200
    * def fondo = response
    * match fondo.status == 'OPEN'
