# ──────────────────────────────────────────────────────────────────────
# L17 · E05 · La tabla de seguridad
#
# Completa la tabla: qué estado recibe cada actor al leer a Lars.
#
# Rellena cada ___ y comprueba con:  academia corregir L17
# Pista: Lars se ve a sí mismo. Ana no debe ni saber que existe. Admin lo ve todo.
# ──────────────────────────────────────────────────────────────────────
@L17 @E05
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
      | lars  | ___ |
      | ana   | ___ |
      | admin | ___ |
