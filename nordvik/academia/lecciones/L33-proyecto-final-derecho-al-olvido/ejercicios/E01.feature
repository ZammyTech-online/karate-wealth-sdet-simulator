# ──────────────────────────────────────────────────────────────────────
# L33 · E01 · Pedir el borrado
#
# Karin pide el borrado de un cliente sin contratos. Completa el estado HTTP y el estado de la solicitud.
#
# Rellena cada ___ y comprueba con:  academia corregir L33
# Pista: Se crea la solicitud. Si nada lo impide, termina en el momento.
# ──────────────────────────────────────────────────────────────────────
@L33 @E01
Feature: L33 E01 · Pedir el borrado

  Scenario: Borrado
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status ___
    And match response.status == ___
    And match response.certificateSha256 == '#regex [0-9a-f]{64}'
