# ──────────────────────────────────────────────────────────────────────
# L26 · E01 · Sin firma, fuera
#
# Manda el webhook sin X-Signature. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L26 @E01 @solucion
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
    Then status 401
    And match response.code == 'INVALID_SIGNATURE'
