# ──────────────────────────────────────────────────────────────────────
# L33 · E05 · Dos veces no
#
# Pide el borrado del mismo cliente otra vez. Completa estado y código.
#
# Rellena cada ___ y comprueba con:  academia corregir L33
# Pista: Ya está hecho: conflicto. El código dice que ya se borró.
# ──────────────────────────────────────────────────────────────────────
@L33 @E05
Feature: L33 E05 · Dos veces no

  Scenario: Repetido
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Primera vez' }
    When method post
    Then status 201
    Given path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Segunda vez' }
    When method post
    Then status ___
    And match response.code == ___
