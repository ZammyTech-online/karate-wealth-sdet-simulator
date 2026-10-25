@interop @security
Feature: Level 04 - Cryptographic Payload Signatures via Java Interop

  Background:
    * url baseUrl
    * def auth = karate.callonce('classpath:com/soderberg/qa/reusable/auth-token.feature')
    * def advisorToken = auth.authToken
    # Java Interop: Instantiate Java Class inside Karate DSL
    * def CryptoHelper = Java.type('com.soderberg.qa.helpers.CryptoSigner')

  Scenario: Successfully approve transfer with valid HMAC-SHA256 signature
    * def transferPayload = '{"fromAccount":"ACC-1001","toAccount":"ACC-9009","amount":25000.00,"currency":"SEK"}'
    # Compute signature through Java method
    * def calculatedSignature = CryptoHelper.signPayload(transferPayload)
    * karate.log('HMAC calculated by Java:', calculatedSignature)

    Given path '/api/v1/pension/transfers'
    And header Authorization = 'Bearer ' + advisorToken
    And header Content-Type = 'application/json'
    And header X-Signature = calculatedSignature
    And request transferPayload
    When method post
    Then status 200
    And match response.status == 'APPROVED'
    And match response.transferRef == '#regex ^TRF-[A-Z0-9]{8}$'

  Scenario: Reject transfer when signature header is forged (401 Unauthorized)
    * def transferPayload = '{"fromAccount":"ACC-1001","toAccount":"ACC-9009","amount":999999.00,"currency":"SEK"}'
    * def forgedSignature = '000000000000000000000000000000000000000000000000000000000000dead'

    Given path '/api/v1/pension/transfers'
    And header Authorization = 'Bearer ' + advisorToken
    And header Content-Type = 'application/json'
    And header X-Signature = forgedSignature
    And request transferPayload
    When method post
    Then status 401
    And match response.error == 'invalid_signature'
