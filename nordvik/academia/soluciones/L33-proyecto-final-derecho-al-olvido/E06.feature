# ──────────────────────────────────────────────────────────────────────
# L33 · E06 · Bloqueado por un plan de pensión
#
# Un cliente con un plan vivo no se puede borrar. Completa el estado de la solicitud y el código del bloqueo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L33 @E06 @solucion
Feature: L33 E06 · Bloqueado por un plan de pensión

  Scenario: Bloqueado
    * def p = call read('_plan.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(p.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    And match response.status == 'BLOCKED'
    And match response.blockers[0].code == 'PENSION_PLAN_ACTIVE'
    Given url customerUrl
    And path '/api/v1/internal/customers', p.customerId
    When method get
    Then match response.status == 'ACTIVE'
