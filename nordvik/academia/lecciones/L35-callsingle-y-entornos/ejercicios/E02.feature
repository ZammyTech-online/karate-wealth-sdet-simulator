# ──────────────────────────────────────────────────────────────────────
# L35 · E02 · Validar propiedad de rol administrativo
#
# Verifica que el rol obtenido desde la sesión cacheada sea 'SYSTEM_ADMIN'.
#
# Rellena cada ___ y comprueba con:  academia corregir L35
# Pista: auth.role debe coincidir
# ──────────────────────────────────────────────────────────────────────
@L35 @E02
Feature: L35 E02 · Validar propiedad de rol administrativo

  Scenario: E02 · Validar rol cacheado
    * def auth = karate.callSingle('this:_mock_auth.feature')
    * match auth.role == ___
