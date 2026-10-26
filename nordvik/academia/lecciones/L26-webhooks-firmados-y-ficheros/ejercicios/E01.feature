# ──────────────────────────────────────────────────────────────────────
# L26 · E01 · Sin firma, fuera
#
# Manda el webhook sin X-Signature. Completa estado y código.
#
# Rellena cada ___ y comprueba con:  academia corregir L26
# Pista: No sabemos quién lo manda. El código habla de una firma no válida.
# ──────────────────────────────────────────────────────────────────────
@L26 @E01
Feature: L26 E01 · Sin firma, fuera

  Scenario: Sin firma
    * text cuerpo =
      """
      {"eventType":"FILE_AVAILABLE","fileId":"cf_e01","employerOrgNumber":"556036-0793","period":"2026-10"}
      """
    Given url clearingAdapterUrl
    And path '/api/v1/webhooks/clearing'
    And header Content-Type = 'application/json'
    And request cuerpo
    When method post
    Then status ___
    And match response.code == ___
