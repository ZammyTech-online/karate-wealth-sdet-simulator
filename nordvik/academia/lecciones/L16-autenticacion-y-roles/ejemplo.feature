# Ejemplo resuelto de L16. Ejecútalo con:  academia ejemplo L16
@ejemplo
Feature: L16 · Tokens y roles

  Scenario: Erik pide su token y lee a su clienta Ana
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = 'asesor.erik'
    And form field password = 'Erik2026!'
    When method post
    Then status 200
    And match response contains { access_token: '#string', token_type: 'Bearer', expires_in: '#number' }
    * def token = response.access_token

    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And header Authorization = 'Bearer ' + token
    When method get
    Then status 200
    And match response.id == ids.ana

  Scenario: Sin token, 401 con su código
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    When method get
    Then status 401
    And match response.code == 'AUTHENTICATION_REQUIRED'

  Scenario: Con el ayudante de login
    * def ana = call read('ejercicios/_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers ana.auth
    When method get
    Then status 200
