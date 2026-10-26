# ──────────────────────────────────────────────────────────────────────
# L23 · E03 · Un escenario mal escrito
#
# status=200 no es un fallo: Nordvik lo rechaza. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L23 @E03 @solucion
Feature: L23 E03 · Un escenario mal escrito

  Scenario: Caos inválido
    Given url uuidUrl
    And path '/health'
    And header X-Chaos-Scenario = 'status=200'
    When method get
    Then status 400
    And match response.code == 'INVALID_CHAOS_SCENARIO'
