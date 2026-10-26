# Ejemplo resuelto de L08. Ejecútalo con:  academia ejemplo L08
@ejemplo
Feature: L08 · Comprobar la forma, no el valor

  Scenario: Un lote de UUID v7
    Given url uuidUrl
    And path '/api/v1/uuids'
    And params { version: 7, count: 3 }
    When method get
    Then status 200
    # Esquema completo: lo fijo por valor, lo variable por forma
    And match response == { version: 7, count: 3, uuids: '#[3]', generatedAt: '#string' }
    And match each response.uuids == '#uuid'
    And match response.uuids[0] == '#regex [0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}'
    And match response.count == '#? _ >= 1 && _ <= 1000'
