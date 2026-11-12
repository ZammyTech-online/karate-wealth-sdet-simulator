# ──────────────────────────────────────────────────────────────────────
# L37 · E02 · Intercepción de errores y limpieza
#
# Define una función de limpieza para resetear el reloj virtual o base de datos en caso de fallo.
#
# Rellena cada ___ y comprueba con:  academia corregir L37
# Pista: Usa una función JS asignada a afterScenario
# ──────────────────────────────────────────────────────────────────────
@L37 @E02
Feature: L37 E02 · Intercepción de errores y limpieza

  Scenario: E02 · Limpieza tras fallo
    * def cleanup = ___
    * def res = cleanup()
    * match res == ___
