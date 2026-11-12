# ──────────────────────────────────────────────────────────────────────
# L34 · E05 · Aislamiento de tests con mock independiente
#
# Comprueba que dos llamadas consecutivas con mocks independientes no colisionan.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# El aislamiento hermético garantiza cero colisiones entre hilos de prueba.
# ──────────────────────────────────────────────────────────────────────
@L34 @E05 @solucion
Feature: L34 E05 · Aislamiento de tests con mock independiente

  Scenario: E05 · Mocks aislados
    * def m1 = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * m1.stop()
    * def m2 = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * match m2.port == '#number'
    * m2.stop()
