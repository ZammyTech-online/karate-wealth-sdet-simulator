# ──────────────────────────────────────────────────────────────────────
# L35 · E03 · Comprobar persistencia idéntica en memoria
#
# Llama a callSingle por segunda vez y confirma que el objeto devuelto contiene la misma propiedad token.
#
# Rellena cada ___ y comprueba con:  academia corregir L35
# Pista: Segunda llamada a karate.callSingle
# ──────────────────────────────────────────────────────────────────────
@L35 @E03
Feature: L35 E03 · Comprobar persistencia idéntica en memoria

  Scenario: E03 · Cache garantizado
    * def s1 = karate.callSingle('this:_mock_auth.feature')
    * def s2 = ___
    * match s1.token == ___
