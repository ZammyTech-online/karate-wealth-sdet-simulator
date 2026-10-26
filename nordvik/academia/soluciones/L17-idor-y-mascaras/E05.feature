# ──────────────────────────────────────────────────────────────────────
# L17 · E05 · La tabla de seguridad
#
# Completa la tabla: qué estado recibe cada actor al leer a Lars.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L17 @E05 @solucion
Feature: L17 E05 · La tabla de seguridad

  Scenario Outline: <quien> lee a Lars
    * def actor = call read('_login.feature') usuarios.<quien>
    Given url customerUrl
    And path '/api/v1/customers', ids.lars
    And headers actor.auth
    When method get
    Then status <estado>

    Examples:
      | quien | estado |
      | lars  | 200 |
      | ana   | 404 |
      | admin | 200 |
