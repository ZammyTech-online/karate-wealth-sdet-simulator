@async @polling
Feature: Level 03 - Asynchronous Pension Rebalancing with Retry-Until

  Background:
    * url baseUrl
    * def auth = karate.callonce('classpath:com/soderberg/qa/reusable/auth-token.feature')
    * def advisorToken = auth.authToken
    * def clientRes = karate.call('classpath:com/soderberg/qa/reusable/create-client.feature')
    * def clientId = clientRes.createdClientId

    # Create portfolio for this client with 100% allocation
    Given path '/api/v1/clients', clientId, 'portfolios'
    And header Authorization = 'Bearer ' + advisorToken
    And request
      """
      {
        name: 'Nordic Growth Pension 2026',
        allocations: [
          { isin: 'SE0001', percentage: 60.0 },
          { isin: 'SE0003', percentage: 40.0 }
        ]
      }
      """
    When method post
    Then status 201
    * def portfolioId = response.id

  Scenario: Trigger async rebalancing order and poll until COMPLETED
    # Step 1: Submit async order -> returns 202 Accepted
    Given path '/api/v1/orders/rebalance'
    And header Authorization = 'Bearer ' + advisorToken
    And request { portfolioId: '#(portfolioId)', strategy: 'DYNAMIC_HARVEST' }
    When method post
    Then status 202
    * def orderId = response.orderId
    * match response.status == 'PROCESSING'

    # Step 2: Senior Polling Pattern using Karate 'retry until'
    # Backend marks order COMPLETED after 3 seconds
    * configure retry = { count: 6, interval: 1000 }
    Given path '/api/v1/orders', orderId, 'status'
    And header Authorization = 'Bearer ' + advisorToken
    And retry until response.status == 'COMPLETED'
    When method get
    Then status 200
    And match response.status == 'COMPLETED'
    And match response.completedAt == '#notnull'
