# ──────────────────────────────────────────────────────────────────────
# L35 · E03 · Comprobar persistencia idéntica en memoria
#
# Llama a callSingle por segunda vez y confirma que el objeto devuelto contiene la misma propiedad token.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Karate indexa el feature path en su runtime JVM y devuelve la referencia ya evaluada.
# ──────────────────────────────────────────────────────────────────────
@L35 @E03 @solucion
Feature: L35 E03 · Comprobar persistencia idéntica en memoria

  Scenario: E03 · Cache garantizado
    * def s1 = karate.callSingle('this:_mock_auth.feature')
    * def s2 = karate.callSingle('this:_mock_auth.feature')
    * match s1.token == s2.token
