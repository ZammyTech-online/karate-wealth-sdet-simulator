# ──────────────────────────────────────────────────────────────────────
# L28 · E05 · Encadenar dos llamadas SOAP
#
# Saca el número del primer plan y pide ese plan en concreto. Completa la expresión XPath y la acción de la segunda llamada.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L28 @E05 @solucion
Feature: L28 E05 · Encadenar dos llamadas SOAP

  Scenario: Primero la lista, luego el plan
    * def p = call read('_persona.feature') { edad: 66 }
    * def pnr = p.pnr
    Given url soapUrl
    And path '/soap/LegacyPensionService'
    And request
      """
      <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:leg="urn:nordvik:legacy:pensions:v1">
        <soapenv:Body>
          <leg:GetLegacyPlansRequest>
            <leg:personalNumber>#(pnr)</leg:personalNumber>
          </leg:GetLegacyPlansRequest>
        </soapenv:Body>
      </soapenv:Envelope>
      """
    When soap action 'GetLegacyPlans'
    Then status 200
    * def numero = karate.xmlPath(response, '//plan[1]/planNumber')
    * def valor = karate.xmlPath(response, '//plan[1]/value')
    Given path '/soap/LegacyPensionService'
    And request
      """
      <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:leg="urn:nordvik:legacy:pensions:v1">
        <soapenv:Body>
          <leg:GetLegacyPlanRequest>
            <leg:personalNumber>#(pnr)</leg:personalNumber>
            <leg:planNumber>#(numero)</leg:planNumber>
          </leg:GetLegacyPlanRequest>
        </soapenv:Body>
      </soapenv:Envelope>
      """
    When soap action 'GetLegacyPlan'
    Then status 200
    And match /Envelope/Body/GetLegacyPlanResponse/plan/value == valor
