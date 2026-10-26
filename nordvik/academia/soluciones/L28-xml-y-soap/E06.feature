# ──────────────────────────────────────────────────────────────────────
# L28 · E06 · El contrato: el WSDL
#
# Pide el WSDL y comprueba que declara la acción GetLegacyPlans. Completa el parámetro de la URL y el operador.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L28 @E06 @solucion
Feature: L28 E06 · El contrato: el WSDL

  Scenario: WSDL
    Given url soapUrl
    And path '/soap/LegacyPensionService'
    And param wsdl = ''
    When method get
    Then status 200
    # 'string' convierte el XML de la respuesta en su texto (karate.toString lo convertiría en JSON)
    * string texto = response
    * match texto contains 'soapAction="GetLegacyPlans"'
