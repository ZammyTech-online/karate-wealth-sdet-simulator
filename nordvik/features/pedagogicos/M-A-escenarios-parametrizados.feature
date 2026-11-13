@pedagogico @mid @orders
Feature: M-A: Tablas de Decisión Parametrizadas para Órdenes de Suscripción
  # [CONTEXTO ACTUARIAL]: Las órdenes de inversión tienen diferentes reglas de validación
  # según el importe mínimo por divisa y el tipo de activo.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'

  Scenario Outline: Validar suscripción de fondos según divisa y monto mínimo
    Given path '/api/v1/orders/validate'
    And request { isin: '<isin>', amount: <amount>, currency: '<currency>' }
    When method post
    Then status <expectedStatus>
    And match response.valid == <isValid>

    Examples:
      | isin    | amount  | currency | expectedStatus | isValid |
      | SE0001  | 1000.0  | SEK      | 200            | true    |
      | SE0001  | 50.0    | SEK      | 422            | false   |
      | EU0092  | 500.0   | EUR      | 200            | true    |
      | EU0092  | 10.0    | EUR      | 422            | false   |
