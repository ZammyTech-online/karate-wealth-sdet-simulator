# ──────────────────────────────────────────────────────────────────────
# L34 · E05 · Aislamiento de tests con mock independiente
#
# Comprueba que dos llamadas consecutivas con mocks independientes no colisionan.
#
# Rellena cada ___ y comprueba con:  academia corregir L34
# Pista: Arranca mock1, páralo, y arranca mock2
# ──────────────────────────────────────────────────────────────────────
@L34 @E05
Feature: L34 E05 · Aislamiento de tests con mock independiente

  Scenario: E05 · Mocks aislados
    * def m1 = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * m1.stop()
    * def m2 = ___
    * match m2.port == ___
    * m2.stop()
