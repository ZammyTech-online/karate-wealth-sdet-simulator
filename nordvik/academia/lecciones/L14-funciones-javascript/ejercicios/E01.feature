# ──────────────────────────────────────────────────────────────────────
# L14 · E01 · Tu primera función
#
# Completa la función doble para que devuelva el doble de x.
#
# Rellena cada ___ y comprueba con:  academia corregir L14
# Pista: Doble es multiplicar por un número muy pequeño.
# ──────────────────────────────────────────────────────────────────────
@L14 @E01
Feature: L14 E01 · Tu primera función

  Scenario: Doblar un número
    * def doble = function(x){ return x * ___ }
    * match doble(21) == 42
