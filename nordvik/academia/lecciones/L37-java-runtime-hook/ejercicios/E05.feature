# ──────────────────────────────────────────────────────────────────────
# L37 · E05 · Java Interop en Hook de Validación
#
# Usa Java.type para invocar una clase utilitaria del sistema (ej. java.lang.System).
#
# Rellena cada ___ y comprueba con:  academia corregir L37
# Pista: Java.type('java.lang.System')
# ──────────────────────────────────────────────────────────────────────
@L37 @E05
Feature: L37 E05 · Java Interop en Hook de Validación

  Scenario: E05 · Java Interop en ciclo de test
    * def System = ___
    * def now = ___
    * match now == ___
