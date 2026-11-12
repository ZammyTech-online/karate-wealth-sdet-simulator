# ──────────────────────────────────────────────────────────────────────
# L38 · E02 · Definir rampa de usuarios virtuales
#
# Modela el perfil de inyección de carga con 100 usuarios en 60 segundos.
#
# Rellena cada ___ y comprueba con:  academia corregir L38
# Pista: Usa un objeto con users y duration
# ──────────────────────────────────────────────────────────────────────
@L38 @E02
Feature: L38 E02 · Definir rampa de usuarios virtuales

  Scenario: E02 · Perfil de inyección
    * def profile = ___
    * match profile.users == ___
    * match profile.durationSec == ___
