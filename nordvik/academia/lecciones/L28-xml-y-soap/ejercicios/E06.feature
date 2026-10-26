# ──────────────────────────────────────────────────────────────────────
# L28 · E06 · El contrato: el WSDL
#
# Pide el WSDL y comprueba que declara la acción GetLegacyPlans. Completa el parámetro de la URL y el operador.
#
# Rellena cada ___ y comprueba con:  academia corregir L28
# Pista: El WSDL se pide con ?wsdl: un parámetro sin valor. Para buscar un texto dentro de otro, contains.
# ──────────────────────────────────────────────────────────────────────
@L28 @E06
Feature: L28 E06 · El contrato: el WSDL

  Scenario: WSDL
    Given url soapUrl
    And path '/soap/LegacyPensionService'
    And param ___ = ''
    When method get
    Then status 200
    # 'string' convierte el XML de la respuesta en su texto (karate.toString lo convertiría en JSON)
    * string texto = response
    * match texto ___ 'soapAction="GetLegacyPlans"'
