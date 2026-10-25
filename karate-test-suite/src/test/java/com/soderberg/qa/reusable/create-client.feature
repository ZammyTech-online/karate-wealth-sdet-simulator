@ignore
Feature: Reusable Dynamic Client Generator with Nordic Personnummer

  Background:
    * def authResult = karate.call('classpath:com/soderberg/qa/reusable/auth-token.feature')
    * def advisorToken = authResult.authToken

  Scenario: Create Unique Wealth Management Client
    * url baseUrl
    * path '/api/v1/clients'
    * header Authorization = 'Bearer ' + advisorToken

    # Dynamic generation of unique Swedish Personnummer (YYYYMMDD-XXXX)
    * def isArgPresent = typeof __arg !== 'undefined' && __arg !== null
    * def randomSuffix = Math.floor(1000 + Math.random() * 9000)
    * def randomDay = Math.floor(10 + Math.random() * 18)
    * def generatedPersonnummer = '198711' + randomDay + '-' + randomSuffix
    * def generatedName = 'Client ' + randomSuffix
    * def generatedEmail = 'client.' + randomSuffix + '@soderberg-test.se'
    * def risk = isArgPresent && __arg.riskProfile ? __arg.riskProfile : 'Balanced'

    * request { nationalId: '#(generatedPersonnummer)', fullName: '#(generatedName)', email: '#(generatedEmail)', riskProfile: '#(risk)' }
    * method post
    * status 201
    * def createdClient = response
    * def createdClientId = response.id
    * karate.log('Created client with ID:', createdClientId)
