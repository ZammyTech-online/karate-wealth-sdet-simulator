# ──────────────────────────────────────────────────────────────────────
# L31 · E02 · Registrar la versión 1
#
# Registra el primer esquema de un evento nuevo. Completa quién puede hacerlo y el estado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L31 @E02 @solucion
Feature: L31 E02 · Registrar la versión 1

  Scenario: v1
    * def admin = call read('_login.feature') usuarios.admin
    * def topic = 'academy.e' + java.util.UUID.randomUUID().toString().substring(0, 6) + '.created'
    Given url schemaRegistryUrl
    And path '/api/v1/subjects', topic, 'versions'
    And headers admin.auth
    And request { schema: { required: ['id'], properties: { id: 'string', amount: 'string' } } }
    When method post
    Then status 201
    And match response.version == 1
