# ──────────────────────────────────────────────────────────────────────
# L38 · E05 · Reutilización de escenarios funcionales
#
# Indica el path del feature funcional que se reutiliza como prueba de carga.
#
# Rellena cada ___ y comprueba con:  academia corregir L38
# Pista: classpath:lecciones/L01-primera-peticion/ejemplo.feature
# ──────────────────────────────────────────────────────────────────────
@L38 @E05
Feature: L38 E05 · Reutilización de escenarios funcionales

  Scenario: E05 · Ruta de feature reutilizado
    * def featurePath = ___
    * match featurePath == ___
