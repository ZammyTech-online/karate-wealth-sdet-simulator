# ──────────────────────────────────────────────────────────────────────
# L18 · E04 · El dinero llega a la caja del cliente
#
# Tras el viaje completo, el efectivo disponible del cliente es el importe ingresado.
# Completa la llamada al ayudante y la espera sobre available.
#
# Rellena cada ___ y comprueba con:  academia corregir L18
# Pista: El ayudante recibe un objeto con importe. El saldo también llega de forma eventual: espera sobre response.available.
# ──────────────────────────────────────────────────────────────────────
@L18 @E04
Feature: L18 E04 · El dinero llega a la caja del cliente

  Scenario: Caja disponible
    * def cliente = call read('_cliente-con-dinero.feature') ___
    * configure retry = { count: 40, interval: 500 }
    Given url cashUrl
    And path '/api/v1/internal/customers', cliente.customerId, 'cash'
    And retry until ___
    When method get
    Then status 200
