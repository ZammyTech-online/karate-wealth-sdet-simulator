# ──────────────────────────────────────────────────────────────────────
# L36 · E03 · Exclusión de escenarios con tilde
#
# Documenta la sintaxis de exclusión para saltar tests etiquetados con @flaky (~@flaky).
#
# Rellena cada ___ y comprueba con:  academia corregir L36
# Pista: El selector de tags usa ~ para negar
# ──────────────────────────────────────────────────────────────────────
@L36 @E03
Feature: L36 E03 · Exclusión de escenarios con tilde

  Scenario: E03 · Exclusión de flaky tests
    * def selector = ___
    * match selector == ___
