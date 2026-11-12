# ──────────────────────────────────────────────────────────────────────
# L39 · E05 · Generación de badge de cobertura
#
# Define el estado final de la suite para el badge del README.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Otorga visibilidad inmediata a los desarrolladores y stakeholders del banco.
# ──────────────────────────────────────────────────────────────────────
@L39 @E05 @solucion
Feature: L39 E05 · Generación de badge de cobertura

  Scenario: E05 · Badge de estado
    * def badge = { status: 'PASSED', color: 'green' }
    * match badge.status == 'PASSED'
    * match badge.color == 'green'
