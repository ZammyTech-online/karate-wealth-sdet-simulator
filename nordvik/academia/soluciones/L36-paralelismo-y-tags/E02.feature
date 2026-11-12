# ──────────────────────────────────────────────────────────────────────
# L36 · E02 · Filtrado condicional por tag en runtime
#
# Comprueba mediante karate.tagSelector o tags nativos que el escenario responde a la suite @senior.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Permite disparar únicamente los módulos financieros aprobados.
# ──────────────────────────────────────────────────────────────────────
@L36 @E02 @solucion
Feature: L36 E02 · Filtrado condicional por tag en runtime

  @senior
  Scenario: E02 · Tag senior
    * def level = 'SENIOR'
    * match level == 'SENIOR'
