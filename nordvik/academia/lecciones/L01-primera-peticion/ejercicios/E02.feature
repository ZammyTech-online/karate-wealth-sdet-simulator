# ──────────────────────────────────────────────────────────────────────
# L01 · E02 · La ruta de la lista de fondos
#
# Completa la ruta para leer la lista de fondos.
#
# Rellena cada ___ y comprueba con:  academia corregir L01
# Pista: Es un texto, así que va entre comillas simples. Empieza por /api/v1.
# ──────────────────────────────────────────────────────────────────────
@L01 @E02
Feature: L01 E02 · La ruta de la lista de fondos

  Scenario: Lista de fondos
    Given url catalogUrl
    And path ___
    When method get
    Then status 200
