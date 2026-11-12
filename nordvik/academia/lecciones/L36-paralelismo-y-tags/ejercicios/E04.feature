# ──────────────────────────────────────────────────────────────────────
# L36 · E04 · Configurar número de hilos paralelos
#
# Define el parámetro de paralelismo para un runner Java con 4 hilos concurrentes.
#
# Rellena cada ___ y comprueba con:  academia corregir L36
# Pista: def hilos = 4
# ──────────────────────────────────────────────────────────────────────
@L36 @E04
Feature: L36 E04 · Configurar número de hilos paralelos

  Scenario: E04 · Hilos paralelos
    * def threads = ___
    * match threads == ___
