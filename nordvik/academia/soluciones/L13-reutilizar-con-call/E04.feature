# ──────────────────────────────────────────────────────────────────────
# L13 · E04 · Solo una vez
#
# En el Background, crea la secuencia una sola vez para todo el fichero.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L13 @E04 @solucion
Feature: L13 E04 · Solo una vez

  Background:
    * def unica = callonce read('_crear-secuencia.feature') { prefix: 'UNA' }

  Scenario: La secuencia existe
    * match unica.response.current == 0
    * match unica.response.prefix == 'UNA'

  Scenario: Es la misma en el segundo escenario
    * match unica.response.prefix == 'UNA'
