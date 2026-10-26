# ──────────────────────────────────────────────────────────────────────
# L05 · E01 · Una variable para el ISIN
#
# Guarda en la variable isin el ISIN del fondo Nordvik Europe ESG (SE0000000005). Cotiza en euros.
#
# Rellena cada ___ y comprueba con:  academia corregir L05
# Pista: Un ISIN es un texto: va entre comillas simples.
# ──────────────────────────────────────────────────────────────────────
@L05 @E01
Feature: L05 E01 · Una variable para el ISIN

  Scenario: El fondo europeo cotiza en euros
    * def isin = ___
    Given url marketUrl
    And path '/api/v1/prices', isin, 'latest'
    When method get
    Then status 200
    And match response.currency == 'EUR'
