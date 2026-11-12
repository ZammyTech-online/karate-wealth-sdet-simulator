# ──────────────────────────────────────────────────────────────────────
# L37 · E02 · Intercepción de errores y limpieza
#
# Define una función de limpieza para resetear el reloj virtual o base de datos en caso de fallo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Garantiza que el estado de la plataforma quede limpio para el siguiente test.
# ──────────────────────────────────────────────────────────────────────
@L37 @E02 @solucion
Feature: L37 E02 · Intercepción de errores y limpieza

  Scenario: E02 · Limpieza tras fallo
    * def cleanup = function(){ return 'CLEAN'; }
    * def res = cleanup()
    * match res == 'CLEAN'
