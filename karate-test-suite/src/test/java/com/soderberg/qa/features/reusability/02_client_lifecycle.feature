@reusability @clients
Feature: Level 02 - Client Lifecycle and Reusable Call Isolation

  Background:
    * url baseUrl
    # Senior Pattern: Use karate.callonce to cache advisor token across scenarios
    * def auth = karate.callonce('classpath:com/soderberg/qa/reusable/auth-token.feature')
    * def advisorToken = auth.authToken

  Scenario: Create new client and verify retrieval by ID
    # Step 1: Call reusable create-client feature
    * def newClientRes = karate.call('classpath:com/soderberg/qa/reusable/create-client.feature', { riskProfile: 'Aggressive' })
    * def clientId = newClientRes.createdClientId

    # Step 2: Query client by Id
    Given path '/api/v1/clients', clientId
    And header Authorization = 'Bearer ' + advisorToken
    When method get
    Then status 200
    And match response.id == clientId
    And match response.riskProfile == 'Aggressive'
    And match response.status == 'ACTIVE'

  Scenario: Prevent duplicate Personnummer registration (409 Conflict)
    # Astrid Lindgren pre-seeded Nordic Personnummer
    Given path '/api/v1/clients'
    And header Authorization = 'Bearer ' + advisorToken
    And request { nationalId: '19850412-1234', fullName: 'Duplicate Astrid', email: 'dup@soderberg.se', riskProfile: 'Balanced' }
    When method post
    Then status 409
    And match response.error == 'client_already_exists'

  Scenario: Reject invalid Swedish Personnummer format (422 Unprocessable Entity)
    Given path '/api/v1/clients'
    And header Authorization = 'Bearer ' + advisorToken
    And request { nationalId: '12345', fullName: 'Bad Format', email: 'bad@soderberg.se', riskProfile: 'Balanced' }
    When method post
    Then status 422
    And match response.error == 'invalid_national_id'
