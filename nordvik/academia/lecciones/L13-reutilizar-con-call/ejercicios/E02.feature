# ──────────────────────────────────────────────────────────────────────
# L13 · E02 · Pasar un argumento
#
# Llama al ayudante para crear una secuencia de pagos, con prefijo PAGO.
#
# Rellena cada ___ y comprueba con:  academia corregir L13
# Pista: El argumento es un objeto JSON con el campo prefix.
# ──────────────────────────────────────────────────────────────────────
@L13 @E02
Feature: L13 E02 · Pasar un argumento

  Scenario: Una secuencia de pagos
    * def resultado = call read('_crear-secuencia.feature') { prefix: ___ }
    * match resultado.response.prefix == 'PAGO'
