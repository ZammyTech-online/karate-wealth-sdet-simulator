# ──────────────────────────────────────────────────────────────────────
# L39 · E04 · Política de fallo ante regresión rota
#
# Valida que el código de salida esperado para un fallo de test sea distinto de 0.
#
# Rellena cada ___ y comprueba con:  academia corregir L39
# Pista: exitCode != 0
# ──────────────────────────────────────────────────────────────────────
@L39 @E04
Feature: L39 E04 · Política de fallo ante regresión rota

  Scenario: E04 · Regla de bloqueo de merge
    * def exitCode = 1
    * match ___ == true
