# ──────────────────────────────────────────────────────────────────────
# L07 · E05 · ¿Qué campo está mal?
#
# Los nombres solo admiten minúsculas, números y guiones. Envía uno con mayúsculas
# y comprueba qué campo señala el error.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L07 @E05 @solucion
Feature: L07 E05 · ¿Qué campo está mal?

  Scenario: Nombre con mayúsculas
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: 'Mayusculas' }
    When method post
    Then status 400
    And match response.code == 'VALIDATION_FAILED'
    And match response.errors[0].field == 'name'
