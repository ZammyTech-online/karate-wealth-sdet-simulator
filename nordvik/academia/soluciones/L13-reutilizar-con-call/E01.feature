# ──────────────────────────────────────────────────────────────────────
# L13 · E01 · Llama al ayudante
#
# Llama a la feature ayudante, que está en la misma carpeta, con el prefijo X.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L13 @E01 @solucion
Feature: L13 E01 · Llama al ayudante

  Scenario: Crear con el ayudante
    * def resultado = call read('_crear-secuencia.feature') { prefix: 'X' }
    * match resultado.response.prefix == 'X'
