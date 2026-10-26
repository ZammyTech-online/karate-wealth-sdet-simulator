# ──────────────────────────────────────────────────────────────────────
# L00 · E04 · Predice el código de algo que no existe
#
# Pedimos el fondo SE9999999999. Su formato es correcto, pero no está en el catálogo. ¿Qué código devolverá?
#
# Rellena cada ___ y comprueba con:  academia corregir L00
# Pista: Es el error más famoso de internet: 'no encontrado'.
# ──────────────────────────────────────────────────────────────────────
@L00 @E04
Feature: L00 E04 · Predice el código de algo que no existe

  Scenario: Un fondo que no existe
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    When method get
    Then status ___
