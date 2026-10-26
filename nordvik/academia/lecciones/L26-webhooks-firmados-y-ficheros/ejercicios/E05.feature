# ──────────────────────────────────────────────────────────────────────
# L26 · E05 · Un fichero, un acuse
#
# Una empresa con un empleado manda un fichero con una línea buena y la misma repetida. Completa cuántas acepta y el código de la repetida.
#
# Rellena cada ___ y comprueba con:  academia corregir L26
# Pista: La primera línea entra. La segunda es el mismo empleado y el mismo mes.
# ──────────────────────────────────────────────────────────────────────
@L26 @E05
Feature: L26 E05 · Un fichero, un acuse

  Scenario: Acuse del fichero
    * def admin = call read('_login.feature') usuarios.admin
    * def org = read('_org.js')
    * def empresa = org()
    Given url employerPortalUrl
    And path '/api/v1/employers'
    And headers admin.auth
    And request { orgNumber: '#(empresa)', name: 'Fjällbygg AB', agreement: 'ITP1' }
    When method post
    Then status 201
    * def llave = response.apiKey
    * def c = call read('_cliente.feature')
    Given path '/api/v1/employer/employees'
    And header X-Employer-Key = llave
    And request { personalNumber: '#(c.nationalId)' }
    When method post
    Then status 201
    Given url clearingSimUrl
    And path '/__admin/files'
    And request { employerOrgNumber: '#(empresa)', period: '2026-10', lines: [ { personalNumber: '#(c.nationalId)', amount: '2500.00', agreement: 'ITP1' }, { personalNumber: '#(c.nationalId)', amount: '2500.00', agreement: 'ITP1' } ] }
    When method post
    Then status 201
    And match response.webhookStatus == 200
    Given path '/__admin/files', response.fileId, 'receipt'
    When method get
    Then status 200
    And match response.accepted == ___
    And match response.rejected[0].code == ___
