# ──────────────────────────────────────────────────────────────────────
# L13 · E02 · Pasar un argumento
#
# Llama al ayudante para crear una secuencia de pagos, con prefijo PAGO.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L13 @E02 @solucion
Feature: L13 E02 · Pasar un argumento

  Scenario: Una secuencia de pagos
    * def resultado = call read('_crear-secuencia.feature') { prefix: 'PAGO' }
    * match resultado.response.prefix == 'PAGO'
