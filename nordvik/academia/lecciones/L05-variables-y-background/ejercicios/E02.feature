# ──────────────────────────────────────────────────────────────────────
# L05 · E02 · Guardar un campo de la respuesta
#
# Guarda el NAV del último precio en la variable nav.
#
# Rellena cada ___ y comprueba con:  academia corregir L05
# Pista: El NAV está en el campo nav de la respuesta.
# ──────────────────────────────────────────────────────────────────────
@L05 @E02
Feature: L05 E02 · Guardar un campo de la respuesta

  Scenario: El NAV es un texto con decimales
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    * def nav = ___
    * assert nav.length > 4
    * match nav contains '.'
