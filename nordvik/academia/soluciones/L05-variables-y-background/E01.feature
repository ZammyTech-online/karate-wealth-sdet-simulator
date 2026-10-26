# ──────────────────────────────────────────────────────────────────────
# L05 · E01 · Una variable para el ISIN
#
# Guarda en la variable isin el ISIN del fondo Nordvik Europe ESG (SE0000000005). Cotiza en euros.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L05 @E01 @solucion
Feature: L05 E01 · Una variable para el ISIN

  Scenario: El fondo europeo cotiza en euros
    * def isin = 'SE0000000005'
    Given url marketUrl
    And path '/api/v1/prices', isin, 'latest'
    When method get
    Then status 200
    And match response.currency == 'EUR'
