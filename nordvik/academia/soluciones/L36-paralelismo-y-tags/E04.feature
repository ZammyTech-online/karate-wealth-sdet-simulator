# ──────────────────────────────────────────────────────────────────────
# L36 · E04 · Configurar número de hilos paralelos
#
# Define el parámetro de paralelismo para un runner Java con 4 hilos concurrentes.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# El número de hilos debe alinearse con los cores de CPU de la máquina o contenedor.
# ──────────────────────────────────────────────────────────────────────
@L36 @E04 @solucion
Feature: L36 E04 · Configurar número de hilos paralelos

  Scenario: E04 · Hilos paralelos
    * def threads = 4
    * match threads == 4
