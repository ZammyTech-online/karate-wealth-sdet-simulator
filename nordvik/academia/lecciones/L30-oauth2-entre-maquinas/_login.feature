# Ayudante compartido: pide un token OAuth2 con el flujo password.
# Uso:  * def erik = call read('_login.feature') usuarios.erik   →   erik.token
@ignore
Feature: Ayudante · pedir un token

  Scenario: Login
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = usuario
    And form field password = clave
    When method post
    Then status 200
    * def token = response.access_token
    * def auth = { Authorization: '#("Bearer " + token)' }
