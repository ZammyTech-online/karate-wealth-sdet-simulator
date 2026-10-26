# ──────────────────────────────────────────────────────────────────────
# L26 · E04 · La llave de la empresa
#
# El admin da de alta una empresa y recibe su API key. Completa el nombre de la cabecera con la que la empresa se identifica.
#
# Rellena cada ___ y comprueba con:  academia corregir L26
# Pista: Es una cabecera X-...-Key propia de empresas (mira el README).
# ──────────────────────────────────────────────────────────────────────
@L26 @E04
Feature: L26 E04 · La llave de la empresa

  Scenario: Empresa con API key
    * def admin = call read('_login.feature') usuarios.admin
    * def org = read('_org.js')
    Given url employerPortalUrl
    And path '/api/v1/employers'
    And headers admin.auth
    And request { orgNumber: '#(org())', name: 'Fjällbygg AB', agreement: 'ITP1' }
    When method post
    Then status 201
    * def llave = response.apiKey
    Given path '/api/v1/employer/employees'
    When method get
    Then status 401
    And match response.code == 'EMPLOYER_KEY_REQUIRED'
    Given path '/api/v1/employer/employees'
    And header ___ = llave
    When method get
    Then status 200
    And match response.count == 0
