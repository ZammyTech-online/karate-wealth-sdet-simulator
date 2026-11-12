# ──────────────────────────────────────────────────────────────────────
# L39 · E01 · Comando Maven de ejecución en pipeline
#
# Define el comando Maven estándar que ejecuta Karate filtrando por tags de regresión.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# El flag karate.options sobreescribe los tags desde la línea de comandos de CI.
# ──────────────────────────────────────────────────────────────────────
@L39 @E01 @solucion
Feature: L39 E01 · Comando Maven de ejecución en pipeline

  Scenario: E01 · Comando de ejecución CI
    * def cmd = 'mvn test -Dkarate.options="--tags @smoke"'
    * match cmd contains 'mvn test'
