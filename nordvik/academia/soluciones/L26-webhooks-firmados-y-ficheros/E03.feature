# ──────────────────────────────────────────────────────────────────────
# L26 · E03 · La firma buena
#
# Firma con el secreto del clearing (e9-clearing-webhook-2026). Completa la llamada al ayudante y el código que demuestra que la firma pasó.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L26 @E03 @solucion
Feature: L26 E03 · La firma buena

  Scenario: Firma correcta
    * def hmac = read('_hmac.js')
    * text cuerpo =
      """
      {"eventType":"FILE_AVAILABLE","fileId":"cf_e03","employerOrgNumber":"556036-0793","period":"2026-10"}
      """
    Given url clearingAdapterUrl
    And path '/api/v1/webhooks/clearing'
    And header Content-Type = 'application/json'
    And header X-Signature = 'sha256=' + hmac('e9-clearing-webhook-2026', cuerpo)
    And request cuerpo
    When method post
    Then status 502
    And match response.code == 'FILE_DOWNLOAD_FAILED'
