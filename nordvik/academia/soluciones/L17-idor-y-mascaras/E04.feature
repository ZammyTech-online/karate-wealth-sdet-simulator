# ──────────────────────────────────────────────────────────────────────
# L17 · E04 · Cumplimiento lo ve todo
#
# Karin (cumplimiento) lee a Ana: el número nacional no lleva ni un asterisco.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L17 @E04 @solucion
Feature: L17 E04 · Cumplimiento lo ve todo

  Scenario: Sin máscara
    * def karin = call read('_login.feature') usuarios.karin
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers karin.auth
    When method get
    Then status 200
    And match response.nationalId !contains '*'
