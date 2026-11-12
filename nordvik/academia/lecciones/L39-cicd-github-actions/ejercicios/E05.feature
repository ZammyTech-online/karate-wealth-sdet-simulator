# ──────────────────────────────────────────────────────────────────────
# L39 · E05 · Generación de badge de cobertura
#
# Define el estado final de la suite para el badge del README.
#
# Rellena cada ___ y comprueba con:  academia corregir L39
# Pista: status: 'PASSED'
# ──────────────────────────────────────────────────────────────────────
@L39 @E05
Feature: L39 E05 · Generación de badge de cobertura

  Scenario: E05 · Badge de estado
    * def badge = ___
    * match badge.status == ___
    * match badge.color == ___
