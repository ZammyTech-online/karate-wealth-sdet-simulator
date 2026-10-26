# ──────────────────────────────────────────────────────────────────────
# L16 · E03 · Manda el token
#
# Ana ya tiene token. Completa la cabecera para leer su propia ficha de cliente.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L16 @E03 @solucion
Feature: L16 E03 · Manda el token

  Scenario: Ana lee su ficha
    * def ana = call read('_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And header Authorization = 'Bearer ' + ana.token
    When method get
    Then status 200
    And match response.id == ids.ana
