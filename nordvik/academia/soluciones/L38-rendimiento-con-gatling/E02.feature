# ──────────────────────────────────────────────────────────────────────
# L38 · E02 · Definir rampa de usuarios virtuales
#
# Modela el perfil de inyección de carga con 100 usuarios en 60 segundos.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Simula la apertura de mercado bancario con llegada paulatina de clientes.
# ──────────────────────────────────────────────────────────────────────
@L38 @E02 @solucion
Feature: L38 E02 · Definir rampa de usuarios virtuales

  Scenario: E02 · Perfil de inyección
    * def profile = { users: 100, durationSec: 60 }
    * match profile.users == 100
    * match profile.durationSec == 60
