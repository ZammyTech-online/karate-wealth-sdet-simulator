# ──────────────────────────────────────────────────────────────────────
# L26 · E02 · Firmado con otro secreto
#
# Una firma bien formada pero con un secreto equivocado tampoco vale. Completa el estado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L26 @E02 @solucion
Feature: L26 E02 · Firmado con otro secreto

  Scenario: Secreto equivocado
    * def hmac = read('_hmac.js')
    * text cuerpo =
      """
      {"eventType":"FILE_AVAILABLE","fileId":"cf_e02","employerOrgNumber":"556036-0793","period":"2026-10"}
      """
    Given url clearingAdapterUrl
    And path '/api/v1/webhooks/clearing'
    And header Content-Type = 'application/json'
    And header X-Signature = 'sha256=' + hmac('no-es-el-secreto', cuerpo)
    And request cuerpo
    When method post
    Then status 401
