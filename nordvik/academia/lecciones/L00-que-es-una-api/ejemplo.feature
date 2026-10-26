# Ejemplo resuelto de L00. Ejecútalo con:  academia ejemplo L00
@ejemplo
Feature: L00 · Mi primera conversación con una API

  Scenario: Pedir el catálogo de fondos y ver qué contesta el servidor
    # 1. ¿A quién hablamos? A product-catalog (la URL viene de karate-config.js)
    Given url catalogUrl
    # 2. ¿Qué recurso queremos? La lista de fondos
    And path '/api/v1/funds'
    # 3. ¿Qué queremos hacer? Leer: GET
    When method get
    # 4. ¿Qué esperamos? 200 = todo bien
    Then status 200
    # 5. Miramos la respuesta: un JSON con la lista y el total
    And print 'El catálogo tiene', response.total, 'fondos'

  Scenario: Pedir un fondo que no existe
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    When method get
    # El formato del ISIN es correcto pero el fondo no existe: 404
    Then status 404
    And print 'El servidor explica:', response.detail
