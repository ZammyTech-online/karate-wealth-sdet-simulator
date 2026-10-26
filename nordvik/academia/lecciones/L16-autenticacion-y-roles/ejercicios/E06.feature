# ──────────────────────────────────────────────────────────────────────
# L16 · E06 · Un solo login por feature
#
# Pide el token una sola vez para todos los escenarios: completa la palabra que lo hace.
#
# Rellena cada ___ y comprueba con:  academia corregir L16
# Pista: Es como call, pero se ejecuta una vez por feature.
# ──────────────────────────────────────────────────────────────────────
@L16 @E06
Feature: L16 E06 · Un solo login por feature

  Background:
    * def admin = ___ read('_login.feature') usuarios.admin

  Scenario: El admin lista usuarios
    Given url iamUrl
    And path '/api/v1/users'
    And headers admin.auth
    When method get
    Then status 200

  Scenario: Y sigue teniendo el mismo token
    * match admin.token == '#string'
