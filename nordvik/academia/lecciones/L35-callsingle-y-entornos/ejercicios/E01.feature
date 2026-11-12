# ──────────────────────────────────────────────────────────────────────
# L35 · E01 · Invocar rutina con karate.callSingle
#
# Llama a _mock_auth.feature usando karate.callSingle y verifica que devuelva el token cacheado.
#
# Rellena cada ___ y comprueba con:  academia corregir L35
# Pista: Usa karate.callSingle('this:_mock_auth.feature')
# ──────────────────────────────────────────────────────────────────────
@L35 @E01
Feature: L35 E01 · Invocar rutina con karate.callSingle

  Scenario: E01 · callSingle básico
    * def auth = ___
    * match auth.token == ___
