# ──────────────────────────────────────────────────────────────────────
# L26 · E03 · La firma buena
#
# Firma con el secreto del clearing (e9-clearing-webhook-2026). Completa la llamada al ayudante y el código que demuestra que la firma pasó.
#
# Rellena cada ___ y comprueba con:  academia corregir L26
# Pista: hmac(secreto, texto). Si la firma pasa, Nordvik intenta descargar un fichero que no existe.
# ──────────────────────────────────────────────────────────────────────
@L26 @E03
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
    And header X-Signature = 'sha256=' + ___
    And request cuerpo
    When method post
    Then status 502
    And match response.code == ___
