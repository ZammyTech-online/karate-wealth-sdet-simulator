# ──────────────────────────────────────────────────────────────────────
# L05 · E04 · Lo común, en el Background
#
# Los dos escenarios usan el mismo servicio. Pon su URL en el Background.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L05 @E04 @solucion
Feature: L05 E04 · Lo común, en el Background

  Background:
    * url marketUrl
    * def isin = 'SE0000000001'

  Scenario: Último precio
    Given path '/api/v1/prices', isin, 'latest'
    When method get
    Then status 200

  Scenario: Historial de marzo de 2026 (22 días hábiles)
    Given path '/api/v1/prices', isin, 'history'
    And param from = '2026-03-01'
    And param to = '2026-03-31'
    When method get
    Then status 200
    And match response.count == 22
