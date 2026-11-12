# ──────────────────────────────────────────────────────────────────────
# L34 · E01 · Arrancar un mock en puerto dinámico
#
# Usa karate.start() para iniciar el mock _mock_demo.feature en un puerto libre (port: 0) y verifica que mock.port sea un número.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# karate.start con port: 0 le pide al sistema operativo un puerto TCP efímero libre.
# ──────────────────────────────────────────────────────────────────────
@L34 @E01 @solucion
Feature: L34 E01 · Arrancar un mock en puerto dinámico

  Scenario: E01 · Arrancar mock dinámico
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * match mock.port == '#number'
    * mock.stop()
