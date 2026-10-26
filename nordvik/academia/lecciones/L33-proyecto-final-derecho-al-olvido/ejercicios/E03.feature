# ──────────────────────────────────────────────────────────────────────
# L33 · E03 · El efecto en la ficha
#
# Después del borrado la ficha queda cerrada. Completa la URL del servicio de clientes y el estado de la ficha.
#
# Rellena cada ___ y comprueba con:  academia corregir L33
# Pista: La ficha vive en customer-registry (customerUrl). Cerrada en inglés.
# ──────────────────────────────────────────────────────────────────────
@L33 @E03
Feature: L33 E03 · El efecto en la ficha

  Scenario: Ficha cerrada
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    Given url ___
    And path '/api/v1/internal/customers', c.customerId
    When method get
    Then status 200
    And match response.status == ___
