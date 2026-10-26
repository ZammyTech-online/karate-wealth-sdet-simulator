# ──────────────────────────────────────────────────────────────────────
# L31 · E03 · Un campo obligatorio nuevo rompe a los consumidores
#
# La v2 añade currency como obligatorio. Completa el estado y el motivo exacto.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L31 @E03 @solucion
Feature: L31 E03 · Un campo obligatorio nuevo rompe a los consumidores

  Scenario: v2 incompatible
    * def admin = call read('_login.feature') usuarios.admin
    * def topic = 'academy.e' + java.util.UUID.randomUUID().toString().substring(0, 6) + '.created'
    Given url schemaRegistryUrl
    And path '/api/v1/subjects', topic, 'versions'
    And headers admin.auth
    And request { schema: { required: ['id'], properties: { id: 'string', amount: 'string' } } }
    When method post
    Then status 201
    Given path '/api/v1/subjects', topic, 'versions'
    And headers admin.auth
    And request { schema: { required: ['id', 'currency'], properties: { id: 'string', amount: 'string', currency: 'string' } } }
    When method post
    Then status 409
    And match response.reasons contains 'campo obligatorio nuevo: currency'
