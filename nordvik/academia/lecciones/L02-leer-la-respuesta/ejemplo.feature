# Ejemplo resuelto de L02. Ejecútalo con:  academia ejemplo L02
@ejemplo
Feature: L02 · Comprobar el contenido de la respuesta

  Scenario: El fondo global tiene los datos esperados
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    # Mirar (no comprueba nada, solo escribe en el log)
    And print response
    # Comprobar texto
    And match response.name == 'Nordvik Global Equity'
    # Comprobar número: sin comillas
    And match response.sri == 4
    # Los campos de este equipo van en snake_case
    And match response.base_currency == 'SEK'
    # Guardar la respuesta en una variable con nombre propio
    * def fondo = response
    * match fondo.status == 'OPEN'
