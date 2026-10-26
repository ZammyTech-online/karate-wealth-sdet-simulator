# ──────────────────────────────────────────────────────────────────────
# L23 · E01 · Un fallo de una sola petición
#
# Pide /health de uuid-service con un 500 inyectado. Completa la cabecera y el código del problema.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L23 @E01 @solucion
Feature: L23 E01 · Un fallo de una sola petición

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: 500 a propósito
    Given url uuidUrl
    And path '/health'
    And header X-Chaos-Scenario = 'status=500'
    When method get
    Then status 500
    And match response.code == 'CHAOS_INJECTED'
