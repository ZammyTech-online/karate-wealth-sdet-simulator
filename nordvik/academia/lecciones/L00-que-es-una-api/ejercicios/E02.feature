# ──────────────────────────────────────────────────────────────────────
# L00 · E02 · Predice el código de éxito
#
# Pedimos la lista de fondos, que existe y está bien pedida. ¿Qué código devolverá el servidor?
#
# Rellena cada ___ y comprueba con:  academia corregir L00
# Pista: Es el código de éxito más típico: empieza por 2.
# ──────────────────────────────────────────────────────────────────────
@L00 @E02
Feature: L00 E02 · Predice el código de éxito

  Scenario: El catálogo responde bien
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status ___
