# ──────────────────────────────────────────────────────────────────────
# L13 · E05 · Llamar en bucle
#
# Crea dos secuencias, A y B, con una sola línea: pasa una lista de argumentos.
#
# Rellena cada ___ y comprueba con:  academia corregir L13
# Pista: Una lista JSON va entre corchetes: [ {...}, {...} ].
# ──────────────────────────────────────────────────────────────────────
@L13 @E05
Feature: L13 E05 · Llamar en bucle

  Scenario: Dos secuencias de golpe
    * def lista = call read('_crear-secuencia.feature') ___
    * match lista == '#[2]'
    * match lista[1].response.prefix == 'B'
