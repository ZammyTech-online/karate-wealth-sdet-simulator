# ──────────────────────────────────────────────────────────────────────
# L35 · E01 · Invocar rutina con karate.callSingle
#
# Llama a _mock_auth.feature usando karate.callSingle y verifica que devuelva el token cacheado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# callSingle memoriza la ejecución y la comparte entre todos los escenarios.
# ──────────────────────────────────────────────────────────────────────
@L35 @E01 @solucion
Feature: L35 E01 · Invocar rutina con karate.callSingle

  Scenario: E01 · callSingle básico
    * def auth = karate.callSingle('this:_mock_auth.feature')
    * match auth.token == 'nordvik-bearer-cache-token-xyz'
