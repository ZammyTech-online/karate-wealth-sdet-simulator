# ──────────────────────────────────────────────────────────────────────
# L26 · E02 · Firmado con otro secreto
#
# Una firma bien formada pero con un secreto equivocado tampoco vale. Completa el estado.
#
# Rellena cada ___ y comprueba con:  academia corregir L26
# Pista: Para Nordvik es igual que no firmar.
# ──────────────────────────────────────────────────────────────────────
@L26 @E02
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
    Then status ___
