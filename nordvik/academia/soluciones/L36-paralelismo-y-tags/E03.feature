# ──────────────────────────────────────────────────────────────────────
# L36 · E03 · Exclusión de escenarios con tilde
#
# Documenta la sintaxis de exclusión para saltar tests etiquetados con @flaky (~@flaky).
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Excluir pruebas inestables mientras se reparan previene falsos positivos en el gate de despliegue.
# ──────────────────────────────────────────────────────────────────────
@L36 @E03 @solucion
Feature: L36 E03 · Exclusión de escenarios con tilde

  Scenario: E03 · Exclusión de flaky tests
    * def selector = '~@flaky'
    * match selector == '~@flaky'
