# ──────────────────────────────────────────────────────────────────────
# L39 · E04 · Política de fallo ante regresión rota
#
# Valida que el código de salida esperado para un fallo de test sea distinto de 0.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# El pipeline falla y protege la rama main contra regresiones.
# ──────────────────────────────────────────────────────────────────────
@L39 @E04 @solucion
Feature: L39 E04 · Política de fallo ante regresión rota

  Scenario: E04 · Regla de bloqueo de merge
    * def exitCode = 1
    * match exitCode != 0 == true
