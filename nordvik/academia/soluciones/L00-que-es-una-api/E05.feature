# ──────────────────────────────────────────────────────────────────────
# L00 · E05 · Predice el código de una petición mal hecha
#
# Ahora pedimos el fondo 'hola', que ni siquiera tiene formato de ISIN.
# No es que no exista: es que la petición está mal formada. ¿Qué código devolverá?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L00 @E05 @solucion
Feature: L00 E05 · Predice el código de una petición mal hecha

  Scenario: Un ISIN mal formado
    Given url catalogUrl
    And path '/api/v1/funds/hola'
    When method get
    Then status 400
