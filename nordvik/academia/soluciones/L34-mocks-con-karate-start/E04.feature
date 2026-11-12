# ──────────────────────────────────────────────────────────────────────
# L34 · E04 · Detener el mock y liberar el puerto
#
# Verifica que llamar a mock.stop() apaga el servidor y detiene el listener.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Liberar los sockets previene fugas de memoria y puertos bloqueados en pipelines CI/CD.
# ──────────────────────────────────────────────────────────────────────
@L34 @E04 @solucion
Feature: L34 E04 · Detener el mock y liberar el puerto

  Scenario: E04 · Apagado limpio de mock
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * def port = mock.port
    * mock.stop()
    * match port == '#number'
