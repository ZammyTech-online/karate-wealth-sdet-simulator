# ──────────────────────────────────────────────────────────────────────
# L02 · E04 · Guarda la respuesta en una variable
#
# Guarda la respuesta completa en la variable fondo y comprueba con ella que el fondo está abierto.
#
# Rellena cada ___ y comprueba con:  academia corregir L02
# Pista: Después de 'method', Karate guarda el cuerpo en una variable que ya conoces.
# ──────────────────────────────────────────────────────────────────────
@L02 @E04
Feature: L02 E04 · Guarda la respuesta en una variable

  Scenario: Tech Growth está abierto a inversiones
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000006'
    When method get
    Then status 200
    * def fondo = ___
    * match fondo.status == 'OPEN'
