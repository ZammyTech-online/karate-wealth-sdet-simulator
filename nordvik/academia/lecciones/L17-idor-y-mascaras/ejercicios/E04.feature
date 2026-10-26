# ──────────────────────────────────────────────────────────────────────
# L17 · E04 · Cumplimiento lo ve todo
#
# Karin (cumplimiento) lee a Ana: el número nacional no lleva ni un asterisco.
#
# Rellena cada ___ y comprueba con:  academia corregir L17
# Pista: Lo contrario de contains en Karate se escribe con una exclamación delante.
# ──────────────────────────────────────────────────────────────────────
@L17 @E04
Feature: L17 E04 · Cumplimiento lo ve todo

  Scenario: Sin máscara
    * def karin = call read('_login.feature') usuarios.karin
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers karin.auth
    When method get
    Then status 200
    And match response.nationalId ___ '*'
