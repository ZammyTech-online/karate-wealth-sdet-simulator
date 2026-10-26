# ──────────────────────────────────────────────────────────────────────
# L16 · E03 · Manda el token
#
# Ana ya tiene token. Completa la cabecera para leer su propia ficha de cliente.
#
# Rellena cada ___ y comprueba con:  academia corregir L16
# Pista: El valor de la cabecera es la palabra Bearer, un espacio y el token.
# ──────────────────────────────────────────────────────────────────────
@L16 @E03
Feature: L16 E03 · Manda el token

  Scenario: Ana lee su ficha
    * def ana = call read('_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And header Authorization = ___
    When method get
    Then status 200
    And match response.id == ids.ana
