# ──────────────────────────────────────────────────────────────────────
# L14 · E03 · Una función en un fichero
#
# Hay una función en formatearSek.js, en la misma carpeta. Léela y úsala.
#
# Rellena cada ___ y comprueba con:  academia corregir L14
# Pista: Igual que con los CSV: read() y el nombre del fichero entre comillas.
# ──────────────────────────────────────────────────────────────────────
@L14 @E03
Feature: L14 E03 · Una función en un fichero

  Scenario: Formatear un importe
    * def formatear = read(___)
    * match formatear(1500) == '1500.00 SEK'
