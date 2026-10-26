# ──────────────────────────────────────────────────────────────────────
# L22 · E06 · Un pasaporte se gasta
#
# Olof emite un pasaporte de una lectura para payments-gateway. Completa la cabecera del destinatario y el estado de la segunda lectura.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L22 @E06 @solucion
Feature: L22 E06 · Un pasaporte se gasta

  Scenario: Lectura única
    * def olof = call read('_login.feature') usuarios.olof
    * def karin = call read('_login.feature') usuarios.karin
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'EMAIL', value: '#("e6" + java.util.UUID.randomUUID().toString().substring(0, 6) + "@example.com")', customerId: '#(ids.ana)' }
    When method post
    Then status 201
    * def token = response.token
    Given url passportsUrl
    And path '/api/v1/passports'
    And headers olof.auth
    And request { token: '#(token)', purpose: 'PAYMENT_EXECUTION', audience: 'payments-gateway' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v1/passports', id, 'read'
    And headers olof.auth
    And header X-Caller-Service = 'payments-gateway'
    When method post
    Then status 200
    Given path '/api/v1/passports', id, 'read'
    And headers olof.auth
    And header X-Caller-Service = 'payments-gateway'
    When method post
    Then status 410
    And match response.code == 'PASSPORT_EXHAUSTED'
