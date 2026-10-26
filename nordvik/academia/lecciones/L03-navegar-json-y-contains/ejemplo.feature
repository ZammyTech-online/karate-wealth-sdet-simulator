# Ejemplo resuelto de L03. Ejecútalo con:  academia ejemplo L03
@ejemplo
Feature: L03 · Moverse por el JSON

  Scenario: Las comisiones del fondo global
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    # La primera comisión (las listas empiezan en 0)
    And match response.fees[0].type == 'MANAGEMENT'
    # Solo nos importan dos campos: contains
    And match response contains { category: 'EQUITY', sri: 4 }
    # Un objeto concreto dentro de la lista
    And match response.fees contains { type: 'MANAGEMENT', percent: '0.45' }
    # Todos los tipos de comisión, sin importar el orden
    And match response.fees[*].type contains only ['EXIT', 'ENTRY', 'MANAGEMENT']
    # Lo que NO debe estar
    And match response.fees[*].type !contains 'PERFORMANCE'
