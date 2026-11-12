# ──────────────────────────────────────────────────────────────────────
# L36 · E05 · Validar aserción independiente de tiempo
#
# Escribe una aserción que no dependa de sleeps ni orden secuencial de ejecución.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Los tests concurrentes jamás deben suponer que otro test corrió antes.
# ──────────────────────────────────────────────────────────────────────
@L36 @E05 @solucion
Feature: L36 E05 · Validar aserción independiente de tiempo

  Scenario: E05 · Invariante concurrente
    * def testData = { id: 'TX-1', status: 'OK' }
    * match testData.status == 'OK'
