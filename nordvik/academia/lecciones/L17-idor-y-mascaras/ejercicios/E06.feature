# ──────────────────────────────────────────────────────────────────────
# L17 · E06 · El cliente no da altas
#
# Ana intenta dar de alta un cliente. Su token no tiene el permiso (scope) necesario: completa el código.
#
# Rellena cada ___ y comprueba con:  academia corregir L17
# Pista: No falla por rol sino porque al token le falta un scope. El código lo dice tal cual.
# ──────────────────────────────────────────────────────────────────────
@L17 @E06
Feature: L17 E06 · El cliente no da altas

  Scenario: Alta sin permiso
    * def ana = call read('_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers'
    And headers ana.auth
    And request { firstName: 'Ana', lastName: 'Clon', nationalId: '19900101-1234', email: 'clon@example.com' }
    When method post
    Then status 403
    And match response.code == ___
