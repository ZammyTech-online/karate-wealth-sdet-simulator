@pedagogico @senior @pensions @saga
Feature: S-B: Verificación de Transacción de Compensación en Saga de Traspaso de Plan
  # [CONTEXTO ACTUARIAL]: En un traspaso de pensión entre entidades (Flytträtt), si la entidad
  # receptora rechaza el alta, el dinero debitado debe reembolsarse inmediatamente a la cuenta de origen.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'

  Scenario: Provocar rechazo en paso 3 y certificar que la saga ejecuta el reembolso (Compensación)
    Given path '/api/v1/pensions/transfer-saga'
    # Forzamos un rechazo regulatorio con un PNR simulado con alerta de embargo
    And request
      """
      {
        sourceAccountId: "ACC-5001",
        targetInstitution: "BLOCKED_INSTITUTION",
        amount: 150000,
        personalNumber: "19800101-9999"
      }
      """
    When method post
    Then status 409
    And match response.sagaStatus == 'COMPENSATED'
    And match response.compensationAction == 'FUNDS_REFUNDED_TO_SOURCE'
    And match response.sourceBalanceRestored == true
