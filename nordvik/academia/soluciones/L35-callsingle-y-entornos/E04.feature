# ──────────────────────────────────────────────────────────────────────
# L35 · E04 · Inyección en cabeceras de petición
#
# Usa el token obtenido para construir una cabecera Authorization: Bearer <token>.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Patrón estándar para autenticar peticiones contra microservicios seguros.
# ──────────────────────────────────────────────────────────────────────
@L35 @E04 @solucion
Feature: L35 E04 · Inyección en cabeceras de petición

  Scenario: E04 · Cabecera construida
    * def auth = karate.callSingle('this:_mock_auth.feature')
    * def headerAuth = 'Bearer ' + auth.token
    * match headerAuth == 'Bearer nordvik-bearer-cache-token-xyz'
