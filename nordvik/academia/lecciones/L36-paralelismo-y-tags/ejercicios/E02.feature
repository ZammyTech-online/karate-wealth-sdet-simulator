# ──────────────────────────────────────────────────────────────────────
# L36 · E02 · Filtrado condicional por tag en runtime
#
# Comprueba mediante karate.tagSelector o tags nativos que el escenario responde a la suite @senior.
#
# Rellena cada ___ y comprueba con:  academia corregir L36
# Pista: Etiqueta @senior
# ──────────────────────────────────────────────────────────────────────
@L36 @E02
Feature: L36 E02 · Filtrado condicional por tag en runtime

  @senior
  Scenario: E02 · Tag senior
    * def level = ___
    * match level == ___
