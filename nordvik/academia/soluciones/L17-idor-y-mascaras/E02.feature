# ──────────────────────────────────────────────────────────────────────
# L17 · E02 · Asesor sin asignación
#
# Erik pide a Sofía, que no es clienta suya. Completa el estado y la frase que explica el motivo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L17 @E02 @solucion
Feature: L17 E02 · Asesor sin asignación

  Scenario: Erik y Sofía
    * def erik = call read('_login.feature') usuarios.erik
    Given url customerUrl
    And path '/api/v1/customers', ids.sofia
    And headers erik.auth
    When method get
    Then status 403
    And match response.code == 'ACCESS_DENIED'
    And match response.detail contains 'no está asignado'
