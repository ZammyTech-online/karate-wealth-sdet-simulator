# ──────────────────────────────────────────────────────────────────────
# L35 · E02 · Validar propiedad de rol administrativo
#
# Verifica que el rol obtenido desde la sesión cacheada sea 'SYSTEM_ADMIN'.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Los claims de rol permiten armar cabeceras Authorization de forma transparente.
# ──────────────────────────────────────────────────────────────────────
@L35 @E02 @solucion
Feature: L35 E02 · Validar propiedad de rol administrativo

  Scenario: E02 · Validar rol cacheado
    * def auth = karate.callSingle('this:_mock_auth.feature')
    * match auth.role == 'SYSTEM_ADMIN'
