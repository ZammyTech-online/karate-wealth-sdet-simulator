@basics @smoke
Feature: Level 01 - Fund Market Discovery and Schema Assertions

  Background:
    * url baseUrl

  Scenario: Verify Public Pension Funds Catalog returns 200 with fuzzy matchers
    Given path '/api/v1/funds'
    When method get
    Then status 200
    And match response.total == '#number'
    And assert response.total > 0
    And match response.items == '#[]'
    
    # Senior Schema Assertion: Each fund must match strict field structure
    And match each response.items ==
      """
      {
        isin: '#regex ^[A-Z]{2}[0-9]{4}$',
        name: '#string',
        assetClass: '#? _ == "Equity" || _ == "Balanced" || _ == "FixedIncome"',
        nav: '#number',
        currency: '#string',
        riskGrade: '#number'
      }
      """

  Scenario: Filter funds by AssetClass and Currency query parameters
    Given path '/api/v1/funds'
    And param assetClass = 'Equity'
    And param currency = 'SEK'
    When method get
    Then status 200
    And match each response.items contains { assetClass: 'Equity', currency: 'SEK' }

  Scenario: Attempt to fetch non-existent ISIN returns 404 Not Found
    Given path '/api/v1/funds/XX9999'
    When method get
    Then status 404
    And match response == { error: 'fund_not_found', isin: 'XX9999' }
