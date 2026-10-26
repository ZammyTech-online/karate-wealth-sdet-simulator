# ──────────────────────────────────────────────────────────────────────
# L31 · E04 · Validar un payload
#
# Valida un evento sin paymentId contra el esquema de payment.received. Completa si es válido y el error.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L31 @E04 @solucion
Feature: L31 E04 · Validar un payload

  Scenario: Payload incompleto
    Given url schemaRegistryUrl
    And path '/api/v1/subjects/payment.received/validate'
    And request { payload: { customerId: 'c1', amount: '100.00' } }
    When method post
    Then status 200
    And match response.valid == false
    And match response.errors contains 'falta paymentId'
