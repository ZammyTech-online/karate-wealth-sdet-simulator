# ──────────────────────────────────────────────────────────────────────
# L39 · E01 · Comando Maven de ejecución en pipeline
#
# Define el comando Maven estándar que ejecuta Karate filtrando por tags de regresión.
#
# Rellena cada ___ y comprueba con:  academia corregir L39
# Pista: mvn test -Dkarate.options='--tags @smoke'
# ──────────────────────────────────────────────────────────────────────
@L39 @E01
Feature: L39 E01 · Comando Maven de ejecución en pipeline

  Scenario: E01 · Comando de ejecución CI
    * def cmd = ___
    * match cmd contains ___
