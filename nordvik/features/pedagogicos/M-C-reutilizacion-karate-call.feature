@pedagogico @mid @auth
Feature: M-C: Orquestación Modular con Invocación karate.call
  # [CONTEXTO ACTUARIAL]: No hacemos login manual en cada test. Invocamos un feature auxiliar
  # una sola vez (karate.callSingle) para inyectar el Bearer Token en la cabecera.

  Background:
    * url 'http://localhost:5125'
    # Simulamos el token obtenido mediante karate.call
    * def authResult = { token: 'eyJhGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.nordvik.test' }
    * header Authorization = 'Bearer ' + authResult.token
    * header Accept = 'application/json'

  Scenario: Consultar movimientos restringidos con sesión corporativa autenticada
    Given path '/api/v1/accounts/ACC-7721/transactions'
    When method get
    Then status 200
    And match response.accountNumber == 'ACC-7721'
    And match response.transactions == '#[]'
