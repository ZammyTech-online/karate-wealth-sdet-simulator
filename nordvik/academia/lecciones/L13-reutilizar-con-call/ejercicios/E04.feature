# ──────────────────────────────────────────────────────────────────────
# L13 · E04 · Solo una vez
#
# En el Background, crea la secuencia una sola vez para todo el fichero.
#
# Rellena cada ___ y comprueba con:  academia corregir L13
# Pista: Es la versión de call que se ejecuta una vez y reutiliza el resultado.
# ──────────────────────────────────────────────────────────────────────
@L13 @E04
Feature: L13 E04 · Solo una vez

  Background:
    * def unica = ___ read('_crear-secuencia.feature') { prefix: 'UNA' }

  Scenario: La secuencia existe
    * match unica.response.current == 0
    * match unica.response.prefix == 'UNA'

  Scenario: Es la misma en el segundo escenario
    * match unica.response.prefix == 'UNA'
