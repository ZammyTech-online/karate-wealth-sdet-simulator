# ──────────────────────────────────────────────────────────────────────
# L37 · E05 · Java Interop en Hook de Validación
#
# Usa Java.type para invocar una clase utilitaria del sistema (ej. java.lang.System).
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Karate permite enlazar bibliotecas de cifrado y cálculo actuarial directamente desde Java.
# ──────────────────────────────────────────────────────────────────────
@L37 @E05 @solucion
Feature: L37 E05 · Java Interop en Hook de Validación

  Scenario: E05 · Java Interop en ciclo de test
    * def System = Java.type('java.lang.System')
    * def now = System.currentTimeMillis()
    * match now == '#number'
