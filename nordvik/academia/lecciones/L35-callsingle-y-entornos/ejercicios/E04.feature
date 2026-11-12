# ──────────────────────────────────────────────────────────────────────
# L35 · E04 · Inyección en cabeceras de petición
#
# Usa el token obtenido para construir una cabecera Authorization: Bearer <token>.
#
# Rellena cada ___ y comprueba con:  academia corregir L35
# Pista: Concatena 'Bearer ' + auth.token
# ──────────────────────────────────────────────────────────────────────
@L35 @E04
Feature: L35 E04 · Inyección en cabeceras de petición

  Scenario: E04 · Cabecera construida
    * def auth = karate.callSingle('this:_mock_auth.feature')
    * def headerAuth = ___
    * match headerAuth == ___
