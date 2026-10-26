# ──────────────────────────────────────────────────────────────────────
# L14 · E04 · Repetir N veces
#
# Genera la lista [0, 10, 20] con karate.repeat.
#
# Rellena cada ___ y comprueba con:  academia corregir L14
# Pista: karate.repeat(n, fn) llama a fn con i = 0, 1, ..., n-1.
# ──────────────────────────────────────────────────────────────────────
@L14 @E04
Feature: L14 E04 · Repetir N veces

  Scenario: Una lista generada
    * def lista = karate.repeat(___, function(i){ return i * 10 })
    * match lista == [0, 10, 20]
