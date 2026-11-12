# ──────────────────────────────────────────────────────────────────────
# L34 · E01 · Arrancar un mock en puerto dinámico
#
# Usa karate.start() para iniciar el mock _mock_demo.feature en un puerto libre (port: 0) y verifica que mock.port sea un número.
#
# Rellena cada ___ y comprueba con:  academia corregir L34
# Pista: karate.start recibe un objeto { mock: '...', port: 0 }
# ──────────────────────────────────────────────────────────────────────
@L34 @E01
Feature: L34 E01 · Arrancar un mock en puerto dinámico

  Scenario: E01 · Arrancar mock dinámico
    * def mock = ___
    * match mock.port == ___
    * mock.stop()
