# ──────────────────────────────────────────────────────────────────────
# L18 · E04 · El dinero llega a la caja del cliente
#
# Tras el viaje completo, el efectivo disponible del cliente es el importe ingresado.
# Completa la llamada al ayudante y la espera sobre available.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L18 @E04 @solucion
Feature: L18 E04 · El dinero llega a la caja del cliente

  Scenario: Caja disponible
    * def cliente = call read('_cliente-con-dinero.feature') { importe: '800.00' }
    * configure retry = { count: 40, interval: 500 }
    Given url cashUrl
    And path '/api/v1/internal/customers', cliente.customerId, 'cash'
    And retry until response.available == '800.00'
    When method get
    Then status 200
