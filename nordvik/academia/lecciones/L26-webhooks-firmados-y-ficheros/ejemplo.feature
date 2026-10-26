# Ejemplo resuelto de L26. Ejecútalo con:  academia ejemplo L26
@ejemplo
Feature: L26 · Firmar un webhook

  Scenario: Firma buena: Nordvik la acepta e intenta descargar el fichero
    * def hmac = read('ejercicios/_hmac.js')
    * text cuerpo =
      """
      {"eventType":"FILE_AVAILABLE","fileId":"cf_ejemplo","employerOrgNumber":"556036-0793","period":"2026-10"}
      """
    Given url clearingAdapterUrl
    And path '/api/v1/webhooks/clearing'
    And header Content-Type = 'application/json'
    And header X-Signature = 'sha256=' + hmac('e9-clearing-webhook-2026', cuerpo)
    And request cuerpo
    When method post
    # La firma pasa; el fichero cf_ejemplo no existe en el clearing: 502 al descargarlo
    Then status 502
    And match response.code == 'FILE_DOWNLOAD_FAILED'
