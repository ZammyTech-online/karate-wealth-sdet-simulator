# ──────────────────────────────────────────────────────────────────────
# L37 · E01 · Configurar afterScenario en línea
#
# Usa configure afterScenario para registrar un mensaje en el log al finalizar.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# afterScenario se ejecuta siempre, incluso si el escenario falló.
# ──────────────────────────────────────────────────────────────────────
@L37 @E01 @solucion
Feature: L37 E01 · Configurar afterScenario en línea

  Scenario: E01 · afterScenario configurado
    * configure afterScenario = function(){ karate.log('cleanup') }
    * def a = 1
    * match a == 1
