# ──────────────────────────────────────────────────────────────────────
# L14 · E04 · Repetir N veces
#
# Genera la lista [0, 10, 20] con karate.repeat.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L14 @E04 @solucion
Feature: L14 E04 · Repetir N veces

  Scenario: Una lista generada
    * def lista = karate.repeat(3, function(i){ return i * 10 })
    * match lista == [0, 10, 20]
