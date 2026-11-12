# ──────────────────────────────────────────────────────────────────────
# L34 · E04 · Detener el mock y liberar el puerto
#
# Verifica que llamar a mock.stop() apaga el servidor y detiene el listener.
#
# Rellena cada ___ y comprueba con:  academia corregir L34
# Pista: Invoca mock.stop() al terminar
# ──────────────────────────────────────────────────────────────────────
@L34 @E04
Feature: L34 E04 · Detener el mock y liberar el puerto

  Scenario: E04 · Apagado limpio de mock
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * def port = mock.port
    * ___
    * match port == '#number'
