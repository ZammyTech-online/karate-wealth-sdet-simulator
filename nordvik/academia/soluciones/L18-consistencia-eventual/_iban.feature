# Ayudante: un IBAN sueco válido y nuevo (la cuenta desde la que "transfiere" el cliente).
@ignore
Feature: Ayudante · IBAN de prueba

  Scenario: IBAN
    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    Then status 201
    * def iban = response.electronic
