# ──────────────────────────────────────────────────────────────────────
# L26 · E06 · El acuse no lleva números en claro
#
# Una línea de alguien que no es cliente. Completa el código de rechazo y el operador que comprueba que el número no sale.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L26 @E06 @solucion
Feature: L26 E06 · El acuse no lleva números en claro

  Scenario: Desconocido y enmascarado
    * def admin = call read('_login.feature') usuarios.admin
    * def org = read('_org.js')
    * def empresa = org()
    Given url employerPortalUrl
    And path '/api/v1/employers'
    And headers admin.auth
    And request { orgNumber: '#(empresa)', name: 'Fjällbygg AB', agreement: 'ITP1' }
    When method post
    Then status 201
    Given url personUrl
    And path '/api/v1/persons'
    And request { minAge: 30, maxAge: 50 }
    When method post
    Then status 200
    * def extrano = response.persons[0].nationalId
    Given url clearingSimUrl
    And path '/__admin/files'
    And request { employerOrgNumber: '#(empresa)', period: '2026-10', lines: [ { personalNumber: '#(extrano)', amount: '1000.00', agreement: 'ITP1' } ] }
    When method post
    Then status 201
    Given path '/__admin/files', response.fileId, 'receipt'
    When method get
    Then status 200
    And match response.rejected[0].code == 'UNKNOWN_EMPLOYEE'
    * def texto = karate.toString(response)
    * match texto !contains extrano
