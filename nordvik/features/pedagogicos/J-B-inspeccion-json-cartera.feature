@pedagogico @junior @accounts
Feature: J-B: Inspección de JSON & Estructuras de Cartera Multidivisa
  # [CONTEXTO ACTUARIAL]: Una cartera bancaria contiene cuentas en coronas (SEK) y euros (EUR).
  # No podemos hacer aserciones de texto plano porque los valores liquidativos (NAV) fluctúan a diario.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Validar cartera del cliente y posiciones mediante operador contains
    Given path '/api/v1/portfolios/P-100234'
    When method get
    Then status 200
    # Verificamos campos raíz sin importar el orden exacto
    And match response contains { portfolioId: 'P-100234', owner: '#string', status: 'ACTIVE' }
    # Verificamos que el array de posiciones contiene activos denominados en SEK
    And match response.holdings == '#[] #object'
    And match response.holdings contains deep { isin: 'SE0001', currency: 'SEK' }
