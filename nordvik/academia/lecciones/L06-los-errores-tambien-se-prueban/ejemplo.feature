# Ejemplo resuelto de L06. Ejecútalo con:  academia ejemplo L06
@ejemplo
Feature: L06 · Probar errores

  Scenario: No hay precio en un festivo
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = '2026-06-19'
    When method get
    Then status 404
    And match response.code == 'NO_PRICE_FOR_DATE'
    And match response.status == 404
    And print 'Pista del servidor:', response.hint

  Scenario: Un importe con coma es inválido
    Given url fxUrl
    And path '/api/v1/convert'
    And params { from: 'EUR', to: 'SEK', amount: '100,00' }
    When method get
    Then status 400
    And match response.code == 'INVALID_AMOUNT'
