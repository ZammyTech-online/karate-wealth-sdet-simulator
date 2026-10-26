# ──────────────────────────────────────────────────────────────────────
# L14 · E01 · Tu primera función
#
# Completa la función doble para que devuelva el doble de x.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L14 @E01 @solucion
Feature: L14 E01 · Tu primera función

  Scenario: Doblar un número
    * def doble = function(x){ return x * 2 }
    * match doble(21) == 42
