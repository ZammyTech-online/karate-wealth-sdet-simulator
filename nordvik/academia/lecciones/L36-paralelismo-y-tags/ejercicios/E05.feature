# ──────────────────────────────────────────────────────────────────────
# L36 · E05 · Validar aserción independiente de tiempo
#
# Escribe una aserción que no dependa de sleeps ni orden secuencial de ejecución.
#
# Rellena cada ___ y comprueba con:  academia corregir L36
# Pista: Verifica invariantes puras de datos
# ──────────────────────────────────────────────────────────────────────
@L36 @E05
Feature: L36 E05 · Validar aserción independiente de tiempo

  Scenario: E05 · Invariante concurrente
    * def testData = { id: 'TX-1', status: 'OK' }
    * match testData.status == ___
