# ──────────────────────────────────────────────────────────────────────
# L08 · E02 · Un esquema completo
#
# Completa el esquema de la respuesta: el último campo es una fecha que cambia en cada llamada.
#
# Rellena cada ___ y comprueba con:  academia corregir L08
# Pista: La fecha llega como texto en JSON.
# ──────────────────────────────────────────────────────────────────────
@L08 @E02
Feature: L08 E02 · Un esquema completo

  Scenario: Esquema de un lote
    Given url uuidUrl
    And path '/api/v1/uuids'
    And params { version: 7, count: 3 }
    When method get
    Then status 200
    And match response == { version: 7, count: 3, uuids: '#array', generatedAt: ___ }
