@pedagogico @senior @security @rfc9457
Feature: S-D: Conformidad con RFC 9457 (Problem Details) y Prevención IDOR
  # [CONTEXTO ACTUARIAL]: Si un atacante intenta acceder al extracto fiscal de otro cliente
  # alterando el ID en la URL, el sistema debe responder HTTP 403 con esquema estándar RFC 9457.

  Background:
    * url 'http://localhost:5125'
    * header Authorization = 'Bearer TOKEN_USER_A'
    * header Accept = 'application/problem+json'

  Scenario: Intentar acceder a la declaración fiscal del Cliente B siendo el Cliente A
    Given path '/api/v1/customers/C-USER-B/tax-statement'
    When method get
    Then status 403
    # Verificamos que la respuesta cumple estrictamente el estándar RFC 9457
    And match response ==
      """
      {
        type: '#regex ^https?://.*',
        title: 'Forbidden',
        status: 403,
        detail: '#string',
        instance: '#string',
        code: 'ACCESS_DENIED_IDOR'
      }
      """
