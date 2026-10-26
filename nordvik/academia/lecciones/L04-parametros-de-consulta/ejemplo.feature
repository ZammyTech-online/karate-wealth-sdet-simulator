# Ejemplo resuelto de L04. Ejecútalo con:  academia ejemplo L04
@ejemplo
Feature: L04 · Filtrar, ordenar y paginar

  Background:
    * url catalogUrl
    * path '/api/v1/funds'

  Scenario: Solo renta variable, la más barata primero
    Given param category = 'EQUITY'
    And param sort = 'fee'
    When method get
    Then status 200
    And match response.total == 7
    And match response.items[0].name == 'Nordvik Sverige Index'

  Scenario: Varios parámetros de una vez
    Given params { currency: 'SEK', max_sri: 2 }
    When method get
    Then status 200
    And match response.items[*].isin contains only ['SE0000000101', 'SE0000000301']

  Scenario: Paginación
    Given params { page: 2, page_size: 5 }
    When method get
    Then status 200
    And match response.page == 2
    And match response.total_pages == 3
