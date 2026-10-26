# ──────────────────────────────────────────────────────────────────────
# L00 · E05 · Predice el código de una petición mal hecha
#
# Ahora pedimos el fondo 'hola', que ni siquiera tiene formato de ISIN.
# No es que no exista: es que la petición está mal formada. ¿Qué código devolverá?
#
# Rellena cada ___ y comprueba con:  academia corregir L00
# Pista: Es el código de 'petición no válida': el primero de los 4xx.
# ──────────────────────────────────────────────────────────────────────
@L00 @E05
Feature: L00 E05 · Predice el código de una petición mal hecha

  Scenario: Un ISIN mal formado
    Given url catalogUrl
    And path '/api/v1/funds/hola'
    When method get
    Then status ___
