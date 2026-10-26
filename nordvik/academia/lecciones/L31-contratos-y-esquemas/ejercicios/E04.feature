# ──────────────────────────────────────────────────────────────────────
# L31 · E04 · Validar un payload
#
# Valida un evento sin paymentId contra el esquema de payment.received. Completa si es válido y el error.
#
# Rellena cada ___ y comprueba con:  academia corregir L31
# Pista: Le falta un campo obligatorio. El error dice 'falta ' y el campo.
# ──────────────────────────────────────────────────────────────────────
@L31 @E04
Feature: L31 E04 · Validar un payload

  Scenario: Payload incompleto
    Given url schemaRegistryUrl
    And path '/api/v1/subjects/payment.received/validate'
    And request { payload: { customerId: 'c1', amount: '100.00' } }
    When method post
    Then status 200
    And match response.valid == ___
    And match response.errors contains ___
