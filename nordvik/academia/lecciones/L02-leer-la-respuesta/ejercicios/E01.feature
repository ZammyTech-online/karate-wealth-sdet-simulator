# ──────────────────────────────────────────────────────────────────────
# L02 · E01 · El nombre de un fondo
#
# El fondo SE0000000002 se llama 'Nordvik Sverige Index'. Compruébalo.
#
# Rellena cada ___ y comprueba con:  academia corregir L02
# Pista: El valor esperado es un texto: va entre comillas simples, con las mayúsculas exactas.
# ──────────────────────────────────────────────────────────────────────
@L02 @E01
Feature: L02 E01 · El nombre de un fondo

  Scenario: Nombre del fondo índice sueco
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000002'
    When method get
    Then status 200
    And match response.name == ___
