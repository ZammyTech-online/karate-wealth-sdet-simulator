# ──────────────────────────────────────────────────────────────────────
# L17 · E02 · Asesor sin asignación
#
# Erik pide a Sofía, que no es clienta suya. Completa el estado y la frase que explica el motivo.
#
# Rellena cada ___ y comprueba con:  academia corregir L17
# Pista: Un empleado sí recibe el motivo. Busca en detail el texto 'no está asignado'.
# ──────────────────────────────────────────────────────────────────────
@L17 @E02
Feature: L17 E02 · Asesor sin asignación

  Scenario: Erik y Sofía
    * def erik = call read('_login.feature') usuarios.erik
    Given url customerUrl
    And path '/api/v1/customers', ids.sofia
    And headers erik.auth
    When method get
    Then status ___
    And match response.code == 'ACCESS_DENIED'
    And match response.detail contains ___
