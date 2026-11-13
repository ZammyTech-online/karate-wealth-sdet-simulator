@pedagogico @senior @payments @idempotency
Feature: S-C: Garantía de Idempotencia en Pagos con Idempotency-Key
  # [CONTEXTO ACTUARIAL]: Si el cliente pulsa dos veces "Transferir" o hay un corte de red,
  # el servidor debe procesar el pago exactamente una sola vez gracias a la clave de idempotencia.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'
    * def uniqueKey = 'IDEMP-' + java.util.UUID.randomUUID()
    * def paymentPayload = { fromAccount: 'ACC-1100', toAccount: 'ACC-2200', amount: 45000, currency: 'SEK' }

  Scenario: Emitir la misma orden dos veces con el mismo Idempotency-Key y evitar doble cargo
    # Primer intento: Petición original (Crea el débito)
    Given path '/api/v1/payments'
    And header Idempotency-Key = uniqueKey
    And request paymentPayload
    When method post
    Then status 201
    * def paymentId = response.paymentId

    # Segundo intento inmediato: Reintento de red con la misma clave
    Given path '/api/v1/payments'
    And header Idempotency-Key = uniqueKey
    And request paymentPayload
    When method post
    # El servidor debe reconocer la clave previa y devolver el mismo recurso sin duplicar el cobro
    Then status 200
    And match response.paymentId == paymentId
    And match response.replayed == true
