# Ejemplo resuelto de L17. Ejecútalo con:  academia ejemplo L17
@ejemplo
Feature: L17 · IDOR y máscaras

  Scenario Outline: <quien> lee a Ana → <estado>
    * def actor = call read('ejercicios/_login.feature') usuarios.<quien>
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers actor.auth
    When method get
    Then status <estado>

    Examples:
      | quien | estado |
      | ana   | 200    |
      | erik  | 200    |
      | karin | 200    |
      | lars  | 404    |

  Scenario: El asesor ve el número nacional enmascarado
    * def erik = call read('ejercicios/_login.feature') usuarios.erik
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers erik.auth
    When method get
    Then status 200
    And match response.nationalId contains '**-****'
