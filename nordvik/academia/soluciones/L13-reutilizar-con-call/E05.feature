# ──────────────────────────────────────────────────────────────────────
# L13 · E05 · Llamar en bucle
#
# Crea dos secuencias, A y B, con una sola línea: pasa una lista de argumentos.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L13 @E05 @solucion
Feature: L13 E05 · Llamar en bucle

  Scenario: Dos secuencias de golpe
    * def lista = call read('_crear-secuencia.feature') [{ prefix: 'A' }, { prefix: 'B' }]
    * match lista == '#[2]'
    * match lista[1].response.prefix == 'B'
