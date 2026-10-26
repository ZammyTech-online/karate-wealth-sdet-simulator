# ──────────────────────────────────────────────────────────────────────
# L14 · E03 · Una función en un fichero
#
# Hay una función en formatearSek.js, en la misma carpeta. Léela y úsala.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L14 @E03 @solucion
Feature: L14 E03 · Una función en un fichero

  Scenario: Formatear un importe
    * def formatear = read('formatearSek.js')
    * match formatear(1500) == '1500.00 SEK'
