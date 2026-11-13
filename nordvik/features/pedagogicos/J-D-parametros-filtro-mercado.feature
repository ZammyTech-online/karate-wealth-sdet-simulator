@pedagogico @junior @market
Feature: J-D: Filtrado de Activos Financieros por Perfil de Riesgo (Query Params)
  # [CONTEXTO ACTUARIAL]: La directiva MiFID II prohíbe ofrecer fondos de alto riesgo
  # a clientes con perfil conservador. El backend debe filtrar exactamente por query params.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Filtrar fondos de renta fija de bajo riesgo denominados en SEK
    Given path '/api/v1/funds'
    And param assetClass = 'FixedIncome'
    And param maxRisk = 2
    And param currency = 'SEK'
    When method get
    Then status 200
    And match each response.items contains { assetClass: 'FixedIncome', currency: 'SEK' }
    And match each response.items[*].riskGrade == '#? _ <= 2'
