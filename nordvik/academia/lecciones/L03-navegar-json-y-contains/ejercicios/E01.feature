# ──────────────────────────────────────────────────────────────────────
# L03 · E01 · El primer elemento de una lista
#
# Comprueba que la primera comisión del fondo SE0000000001 es la de gestión (MANAGEMENT).
#
# Rellena cada ___ y comprueba con:  academia corregir L03
# Pista: ¿En qué número empiezan las listas en JSON y JavaScript?
# ──────────────────────────────────────────────────────────────────────
@L03 @E01
Feature: L03 E01 · El primer elemento de una lista

  Scenario: La primera comisión es la de gestión
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    And match response.fees[___].type == 'MANAGEMENT'
